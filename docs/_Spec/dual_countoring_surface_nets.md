This is an implementation of surface nets with almost-dual-countoring written in GLSL:

if you simply trilinear-filter an sdf over a square lattice, you already get the Marching-cubed-surfaces distanceFunction (not its surface net) to a similar (but different?) surface-net, therefore, this whole rather slow surface-net approach is quite superfluous.

It is a weird compromise between marching cubes and DualContouring.

This is NOT quite Dual-Contouring, but almost Dual-Contouring.
Dual-Contouring also preserves the surface-Normal==gradient at every lattice-intersection (as good as possible, minimizing for quadratic-errors).
Dual-Contouring is better at preserving any sharp corner from (rasterized input data) (even if it is not lattice-aligned, which is called "true flat" among Astroneer-fans (Astroneer is Marching-Cubes on steroids, without any dual-Contouring)

Now that it uses a buffer, all it lacks to perform DualContouring is a qef() solver, to minimize the QuadraticError(function).
qef() introduction is:
http://www.pouet.net/topic.php?which=10776&page=1#c517840
https://github.com/nickgildea/qef

the QEF-minimizing/solving however does have some bad cases, where you ideally would need more than vertex in a cube, and some cases have ambiguity, to be resolved by the dual-Integral of the rasterized volume.
And these finicky/ambiguous details (or solving for dual integral) is usually where most people give up on DualContouring, so there is no good open-surce realtime dual-contouring demo. (also because the qef solver may just be significantly slower, and needs smart gpu-optimizazions (i bet fourier analysis or AI may help to estimate faster and precise enough dual integrals))

yes dual contouring would be better(at handling manifolds), but it's complex.
Trilinear filtering is maybe simpler and there are also examples by Oneshade.
I simply prefer surface nets because vertex are almost equidistant, and surfaces are quads instead of triangles.
I don't think it's slow (buffer B uses 3ms per frame on full resolution); in this weird demo it has the advantage that vertex position and connection bitmask can be easily saved in one pixel.

```glsl

const float L= 30., //cube size
            N= 30.; //number of voxel x side (TOT=N^3)

vec3 offset = floor(vec3(0.,0.,-L/2.));
const vec2 packedChunkSize= ceil(sqrt(N) * vec2(1.25 ,0.8)); // iResolution.xy/iResolution.yx 
const float heightLimit = packedChunkSize.x * packedChunkSize.y; //must be > N

//-----------------------------------------
// VOXEL CACHE FUNCTIONS from fb39ca4

vec2 unswizzleChunkCoord(vec2 storageCoord) {
 	vec2 s = floor(storageCoord);
    float dist = max(s.x, s.y);
    float o = floor(dist / 2.);
    float neg = step(0.5, mod(dist, 2.)) * 2. - 1.;
    return neg * (s - o);
}

vec2 swizzleChunkCoord(vec2 chunkCoord) {
    vec2 c = chunkCoord;
    float dist = max(abs(c.x), abs(c.y));
    vec2 c2 = floor(abs(c - 0.5));
    float o = max(c2.x, c2.y);
    float neg = step(c.x + c.y, 0.) * -2. + 1.;
    return (neg * c) + o;
}

float calcLoadDist(vec2 iResolutionxy) {
    vec2  chunks = floor(iResolutionxy / packedChunkSize); 
    float gridSize = min(chunks.x, chunks.y);    
    return floor((gridSize - 1.) / 2.);
}

vec3 texToVoxCoord(vec2 textelCoord, vec3 offset) {

    vec2 packedChunkSize= packedChunkSize;
	vec3 voxelCoord = offset;
    voxelCoord.xy += unswizzleChunkCoord(textelCoord / packedChunkSize);
    voxelCoord.z += mod(textelCoord.x, packedChunkSize.x) + packedChunkSize.x * mod(textelCoord.y, packedChunkSize.y);
    return voxelCoord;
}

vec2 voxToTexCoord(vec3 voxCoord) {
    vec2 packedChunkSize= packedChunkSize;
    vec3 p = floor(voxCoord);
    return swizzleChunkCoord(p.xy) * packedChunkSize + vec2(mod(p.z, packedChunkSize.x), floor(p.z / packedChunkSize.x));
}

vec4  getVoxel(vec3 p,sampler2D iChannel,vec3 resolution) {
    p.z-= offset.z;
    if(p.z>heightLimit || p.z<0.){return vec4(0.);}  
    return texelFetch(iChannel, ivec2(voxToTexCoord(p))  , 0); 
    //return texture(iChannel, (floor(voxToTexCoord(p)) + 0.5) /  (floor (resolution.xy)), 0.0);

}

//-----------------------------

//Iq
vec2 boxIntersection( in vec3 ro, in vec3 rd, in vec3 rad) 
{
    vec3 m = 1.0/rd;
    vec3 n = m*ro;
    vec3 k = abs(m)*rad;
    vec3 t1 = -n - k;
    vec3 t2 = -n + k;

    float tN = max( max( t1.x, t1.y ), t1.z );
    float tF = min( min( t2.x, t2.y ), t2.z );
	
    if( tN>tF || tF<0.0) return vec2(-1.0); 
    
    //vec3 normal = -sign(rd)*step(t1.yzx,t1.xyz)*step(t1.zxy,t1.xyz);

    return vec2( tN, tF );
}

float sdBox( vec3 p, vec3 b )
{
  vec3 q = abs(p) - b;
  return length(max(q,0.0)) + min(max(q.x,max(q.y,q.z)),0.0);
}


float sdCapsule( vec3 p, vec3 a, vec3 b)
{
  vec3 pa = p - a, ba = b - a;
  float h = clamp( dot(pa,ba)/dot(ba,ba), 0.0, 1.0 );
  return length( pa - ba*h ) ;
}


//ISOSURFACE FUNCTION 

float smin(float a, float b, float k)
{
    float h = clamp(0.5+0.5*(b-a)/k, 0.0, 1.0);
    return mix(b, a, h) - k*h*(1.0-h);
}

float smax(float a, float b, float k)
{
    return smin(a, b, -k);
}

//https://www.shadertoy.com/view/4tVSzR
float map(vec3 p)
{
    p*=.5;
    float time=mod(iTime, 15.0)+2.;
  
    // Shane's variation
    float d0=dot(sin(p),cos(p.yzx));
       
    float d2=length(p)-min(8.,time);
    float d=smax(-d0,d2,3.);

    return d;
}


void mainImage( out vec4 fragColor, in vec2 fragCoord )
{   
    vec3 voxelCoord = texToVoxCoord(floor(fragCoord), offset); 
   
    if(iFrame<300) { 
        // gyroiid for the first 5 seconds
        float data =map(voxelCoord);
  
        fragColor = vec4(data);
    }
    else
    {
        //pulsating after 5 secs
        fragColor = getVoxel( voxelCoord,iChannel0,iChannelResolution[0]) +sin(float(iFrame)/60.)*.01;
    }

}

// SURFACE NET BUFFER


 struct GRIDCELL{     //calculated for each cube in the following sequence:
   vec3 p[8];         //  1. cube vertex positions
   float val[8];      //  2. isosurface value at each cube vertex
   float nVertex;     //  3. number of vertex where val[]>0; if 0<nVertex<8  => surface cube
   float nEdge;       //  4. number of cube edges with crossing 
   vec3 m;            //  5. surface vertex, as an average of all crossing edge positions
   int dirs;          //  6. 6bit bitmask of surface edges from each cube;                        
} ;


const vec3 v[8] =vec3[8]
(
   vec3(0,0,0), vec3(1,0,0),vec3(1,1,0),vec3(0,1,0),
   vec3(0,0,1), vec3(1,0,1),vec3(1,1,1),vec3(0,1,1)
);

const int  e[24] =int[24](
   0,1,   1,2,  2,3,   3,0, 
   4,5,   5,6,  6,7,   7,4,   
   0,4,   1,5,  2,6,   3,7);

const vec3 dir[6] =vec3[6]
(
   vec3(1,0,0), vec3(0,1,0),vec3(0,0,1),
   vec3(-1,0,0), vec3(0,-1,0),vec3(0,0,-1)
);


int gFrame=0; 
void  getSurface(vec3 c, inout GRIDCELL g,float csz)
{
    
    g.m=vec3(0.);
    g.nVertex=0.;
    g.nEdge=0.;
    g.dirs=0;

    //gFrame unrolling fails here...
    for(int i=0;i<8;i++)
    {

        //1. cube vertex positions
        vec3 vp=c*csz+  v[i]*csz ;
        g.p[i]=vp;

        //  2. isosurface value at each cube vertex
        float val = //map(vp);
                    getVoxel( c+  v[i] ,iChannel0,iChannelResolution[0]).x;
         g.val[i]=val;
        
        //3. number of vertex where val[]>0;
        g.nVertex+= (val<=0.?1.:0.);

    }

    c*=csz;
    
     if(g.nVertex>0. && g.nVertex<8.)
     {

          for(int i=gFrame;i<24;i+=2)
          {
              //  isosurface weights at each cube edge vertexes
              float d1 = g.val[e[i]],
                  d2 = g.val[e[i+1]],
                  d= d1/(d1-d2);

            //  4. number of cube edges with crossing 
             if(d1*d2<0.){
                 g.nEdge++;
                 g.m+= g.p[e[i+1]]*d + g.p[e[i]]*(1.-d);
                 
                 for(int k =gFrame;k<6;k++) {
                     
                     //  6. 6bit bitmask of surface edges from  each cube
                     if(dot((g.p[e[i+1]] +g.p[e[i]])/2.- c -csz/2. , dir[k] )<0. )  g.dirs=g.dirs | (1<<k); 
                     
                 }
             }
          }
         
        //  5. surface vertex, as an average of all crossing edge positions
         g.m/= g.nEdge;
         g.m = min(max(g.m,c),c+csz); //must be inside the cube
         //g.m=c+csz/2.; //orthogonal connections
    } else if(g.nVertex==8. ){
        g.dirs=64;g.m=c+csz/2.;
    }
}


void mainImage( out vec4 fragColor, in vec2 fragCoord )
{
    vec3 voxelCoord = texToVoxCoord(floor(fragCoord), offset); 
       
    GRIDCELL g;     
    getSurface(voxelCoord,g,.5);  
    fragColor = vec4(g.m,g.dirs);

}

//Surface Nets II by Kastorp
//
//algorithm https://www.shadertoy.com/view/3tKyzt
//rendering https://www.shadertoy.com/view/7djGDw
//
//TODO: make isosurface editable
//------------------------------------------------

int gFrame=0;
float csz;
//--------------------------


const vec3 dir[6] =vec3[6]
(
   vec3(1,0,0), vec3(0,1,0),vec3(0,0,1),
   vec3(-1,0,0), vec3(0,-1,0),vec3(0,0,-1)
);

int     g_dirs;
vec3[6] g_ng;
vec3    g_m;

const float TK=.12; //edge thickness 
const bool cover=true;
vec2 mmin(vec2 a, vec2 b) {return (a.x<b.x?a:b);}

vec2 mapVoxel(in vec3 p) {


       vec2 shape = vec2(100.,0.);
       if(g_dirs>=64) return vec2(sdBox(p- g_m,vec3(.5*csz)),1.);
       for(int k =gFrame;k<6;k++) {
          //show  surface edges      
          if((g_dirs & (1<<k))>0) shape=mmin(shape, vec2( sdCapsule(p,g_m,g_ng[k] )-csz*TK/2. ,2.));
       } 

        //show surface vertex 
       shape=mmin(shape,vec2( length(p- g_m)- csz*TK ,3.)); 
      
       //simple coloring
       return shape;
}

vec3 getNormal(in vec3 p) {
    vec3 n=vec3(0.);
    for(int i=gFrame;i<=2;i++){
        vec3 e=  0.001* ((i==0)?vec3(1,0,0):(i==1)?vec3(0,1,0):vec3(0,0,1));
        for(float j=-1.;j<=1.;j+=2.) n+= j*e* mapVoxel(p + j* e).x ;
    }
    return normalize(n);
}

vec4 VoxelHitPos(vec3 pos, vec3 ro, vec3 rd){
    vec3 ri = 1.0/rd;
	vec3 rs = sign(rd);
    vec3 mini = (pos-ro + 0.5*csz - csz*0.5*vec3(rs))*ri;
    float t=  max ( mini.x, max ( mini.y, mini.z ) );
    return vec4(t*rd+ro,t);
}

vec3 rayDirection(vec3 cameraDir, vec2 uv){
    
    vec3 cameraPlaneU = vec3(normalize(vec2(cameraDir.y, -cameraDir.x)), 0);
    vec3 cameraPlaneV = cross(cameraPlaneU, cameraDir) ;
	return normalize(cameraDir + uv.x * cameraPlaneU + uv.y * cameraPlaneV);

}

vec4 trace(vec3 ro,vec3 rd)
{
    //RAYTRACING BOUNDING BOX
    vec2 tb= boxIntersection(  ro,  rd, vec3(csz*N*.50+.00001) ) ;
    if(tb.y<0.) return vec4(0.);
    
    
    //VOXEL TRAVERSAL
    vec3 rs= sign(rd);
    vec3 ri = 1./rd;
	vec3 rp=ro +  max(tb.x,0.)*rd;  
    vec3 mp=floor(rp/csz)*csz;
    vec3 sd = (mp-rp + 0.5*csz + sign(rd)*0.5*csz) *ri;
    vec3 mask=vec3(0.);     
    for (int i = 0; i < 200; i++) {
         
        mask = step(sd.xyz, sd.yzx) * step(sd.xyz, sd.zxy);
		sd += mask *  rs *ri*csz;
        mp += mask *  rs*csz;
        
        rp = VoxelHitPos(mp,rp,rd).xyz+rd*.0001; 
        if(length(rp-ro)>tb.y) break; //outside bounding box
        

       vec4 data = getVoxel( mp/csz,iChannel0,iChannelResolution[0]); 
        g_dirs =int(data.a); 
        if(g_dirs>0 )  {
            
            g_m= data.xyz;       
            
           if(g_dirs<64) for(int k =gFrame;k<6;k++) {
                     
              if((g_dirs & (1<<k))>0) {
              
                  //get neighbour surface vertex along each direction
                  g_ng[k] = getVoxel( mp/csz-dir[k],iChannel0,iChannelResolution[0]).xyz; 
                                                  
               } 
           }
            
            //SDF RAYMARCHING INSIDE VOXEL
            float t = 0.0;          
            for (int iters=gFrame; iters < 30; iters++) {

                vec3 p = rp + rd * t;

                if(sdBox(p-mp-.5*csz,vec3(.5*csz))>0.) break;
               
                vec2 d = mapVoxel(p);
                if (d.x < 0.001 ) return vec4(p,d.y);
 
                t += d.x;
            }
        }
	} 
    return vec4(tb.x,tb.y,0.,0.);
}

void mainImage(out vec4 fragColor, in vec2 fragCoord) {

    csz = .5;

    vec2 uv = (fragCoord - 0.5 * iResolution.xy) / iResolution.y;
    vec2  m =  iMouse.x<=0. ? vec2(-0.3): (iMouse.xy - 0.5 * iResolution.xy) / iResolution.y*3.14;
    
    gFrame=min(iFrame,0);

    vec3 ro = vec3(sin(m.y+.76) * cos(-m.x), sin(m.y+.76) * sin(-m.x), cos(m.y +.76))*15.;
    vec3 cd = -normalize(ro); 
    vec3 rd = rayDirection(cd ,uv);
    
    vec4 r= trace(ro,rd);
    
    if(r.a>0. ){
            vec3 p =r.xyz, 
                 n = getNormal(p);
                          
             fragColor.rgb = (.3+ normalize(p)*.7)*(r.a>2.?1.:.5);
                         
             //https://www.shadertoy.com/view/ttGfz1
             fragColor.rgb *= length(sin(n*2.)*.5+.5)/sqrt(3.)*smoothstep(-1.,1.,n.z);
             fragColor = vec4(sqrt(clamp(fragColor, 0., 1.)));      
    }
    
    else fragColor =mix(vec4(0.4,0.4,0.7,1.0),vec4(.2), (r.y-r.x)/L*1.2);
    
}
```

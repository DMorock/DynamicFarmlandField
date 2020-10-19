using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * FARMLAND FIELD SIMULATION by DMorock
 * Hi poly mesh LOD 0
 * 4 LODs
 * Not a part of the Unity terrain
 * Do not disappear frought distance
 * The density, the irregularity and the mask seating plants
 * Low memory using (position of every veg instance calculates on the fly by simply rules, don't stores)
 * Dynamically catting
 * Dynamic objects physic reaction
 * Wind
 */
[ExecuteAlways]
[AddComponentMenu("3D Object/Dynamic Farmland Field")]
public class DynamicFarmlandFieldScript : MonoBehaviour
{
    //Vegetable instance lods
    [System.Serializable]
    public struct VegetationInstance
    {
        [Tooltip("Highlevel detailed poly mesh")]
        public Mesh Lod1; //Poly mesh
        public Vector3 Lod1Rotation;
        public Vector3 Lod1Scale;
        [Tooltip("Highlevel detailed material")]
        public Material Lod1Mat;
        [Tooltip("Distance to switch to less detail level")]
        [Min(0)]
        public float Lod1to2SwitchDistance;
        [Tooltip("Cross billboard material for second LOD")]
        public Material Lod2; //Cross billboard
        [Tooltip("Distance to switch to less detail level")]
        [Min(0)]
        public float Lod2to3SwitchDistance;
        [Tooltip("Camera align billboard material for third LOD")]
        public Material Lod3; //Camera align billbord
        [Tooltip("Distance to switch to less detail level")]
        [Min(0)]
        public float Lod3to4SwitchDistance;
        [Tooltip("Horizontal plane material for fourth LOD")]
        public Material Lod4; //Horizontal plane

    };
    public VegetationInstance WholePlant;
    public VegetationInstance CuttedPlant;
    /*[Tooltip("The power of random noise to instance position")]
    public float PositionRandomization; //The power of random noise to instance position*/
    [Tooltip("The power of random noise to instance size")]
    [Min(0.000001f)]
    public float SizeRandomization;  //The power of random noise to instance size
    [Tooltip("The power of random noise to instance color (from color 01 to color 02 in color space)")]
    [ColorUsageAttribute(false, true)]
    public Color ColorRandomization01; //The power of random noise to instance color
    [ColorUsageAttribute(false, true)]
    public Color ColorRandomization02; //The power of random noise to instance color

    [Space(10)]
    //Field space
    [Tooltip("Size of the plane area")]
    [Min(0.0001f)]
    public float FieldLength = 100.0f; //Size the area
    [Min(0.0001f)]
    public float FieldWidth = 100.0f; //Size the area
    [Tooltip("Size of the minimal quad of vegetations (World units)")]
    [Min(0.0001f)]
    public float MinimalQuad = 1;  //Minimal field step square unit (size of minimal quad of vegetations in calculations, accuracy of (terrain) height and collision determination)
    [Tooltip("Quantity of vegetation instances on MinimalQuad")]
    [Min(1)]
    public int Density = 10;     //Quantity of vegetation instances on MinimalQuad
    [Tooltip("Seed for the noize generator for the irregularity planting")]
    public int RandomSeed; //Seed for the noize generator for the irregularity planting
    [Tooltip("Mask of planitng on MinimalQuad, white = 100% density, grey = 50%, black = 0%")]
    public Texture2D PlantMask; //Mask of planting on MinimalQuad, white = 100% density, grey = 50%, black = 0%
    [Tooltip("Power and direction of wind (world space)")]
    public Vector3 Wind; //Power and direction of wind (world space)

    [Tooltip("The object transform (commonly main camera) that is point of view or observer of field")]
    public Transform ObjectOfView;

    [Tooltip("Use it if size of LODs doesn't match LOD1 size")]
    [Min(0.0001f)]
    public float SizeCheat = 1.0f; //Cheat
    [Tooltip("Debug visualization")]
    public bool DebugMode; //Debug

    //Point of plant growing, plants are generating runtime and transmiting to shader
    //for apropriate LOD level
    //the less detailed LOD has the simple generation rules. This is center point of MinimalQuad.
    struct VegetationPoint
    {
        public Vector3 position; //World space point position

        public enum eVEGPOINTLOD
        {
            eVPHIGH = 0,
            eVPCROSS,
            eVPBILLBOARD,
            eVPPLANE,
            eVPUNKNOWN
        }
        public eVEGPOINTLOD lodLevel; //level of details at the point

        public enum eVEGPOINTTYPE
        {
            eVPWHOLE = 0,
            eVPCUTTED,
            eVPCRUMPLED, //(by physic object)
            eVPDELETED,
            eVPUNKNOWN
        }
        public eVEGPOINTTYPE eType;
    };
    VegetationPoint[] vegetationPoints;

    bool IsReady;
    bool bNeedLODComp;
    Bounds FieldBounds;

    //Mesh meshLOD2;
    ComputeBuffer computebufferLOD2;
    ComputeBuffer computebufferLOD3;
    ComputeBuffer computebufferLOD4;

    //Realy vegetations inside vegpoints quad quantity of density 
    VegetationPoint[] vegpointsLOD1; int vpLOD1Q = 0;
    VegetationPoint[] vegpointsLOD2; int vpLOD2Q = 0;
    VegetationPoint[] vegpointsLOD3; int vpLOD3Q = 0;
    VegetationPoint[] vegpointsLOD4; int vpLOD4Q = 0;

    //Size of plant
    Vector2 PlantSize;

    //Struct that forvards to shaders throught compute buffers
    struct PlantPointToCB
    {
        public Vector3 pos;
        public Vector2 size;
        public Vector3 color;
    };

    //Random vegpoint shot
    PlantPointToCB[] RandomShot;


    //---------------------
    //Editor only staff
#if UNITY_EDITOR
    Mesh meshVis; //Farmfield area Editor visualizer
    //Generate plane visualizing farmfield area in Editor only
    private void GenerateVisMesh()
    {
        if (meshVis == null)
        {
            meshVis = new Mesh();
        }
        else
        {
            meshVis.Clear();
        }

        //Quantity of vertices matrix (and array.length), not world scale size in units
        int xSize = Mathf.FloorToInt(FieldLength / MinimalQuad);
        int ySize = Mathf.FloorToInt(FieldWidth / MinimalQuad);

        if ((xSize * ySize) > 65535)
        {
            //Debug.LogWarning("Dynamic Farmland Field quad quantity > 65535! Too many quads. Making the Minimal quad size smaller.");
            if (xSize > 255) xSize = 255;
            if (ySize > 255) ySize = 255;
        }
        float myMinQuadX = FieldLength / xSize;
        float myMinQuadY = FieldWidth / ySize;

        Vector3[] vertices = new Vector3[(xSize + 1) * (ySize + 1)];
        Vector2[] uv = new Vector2[vertices.Length];
        Vector4[] tangents = new Vector4[vertices.Length];
        Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
        for (int i = 0, y = 0; y <= ySize; y++)
        {
            for (int x = 0; x <= xSize; x++, i++)
            {
                //Check height under terrain
                float zHeight = transform.position.y;
                RaycastHit hit;
                // Does the ray intersect any objects below
                Vector3 posRayCast = new Vector3(0, 0, 0);
                posRayCast = transform.TransformPoint(x * myMinQuadX, 1000.0f, y * myMinQuadY);
                if (Physics.Raycast(posRayCast, transform.TransformDirection(Vector3.down), out hit, Mathf.Infinity))
                {
                    posRayCast.y = posRayCast.y - hit.distance;
                }

                vertices[i] = transform.InverseTransformPoint(posRayCast);
                uv[i] = new Vector2((float)x / xSize, (float)y / ySize);
                tangents[i] = tangent;
            }
        }
        meshVis.vertices = vertices;

        int[] triangles = new int[xSize * ySize * 6];
        for (int ti = 0, vi = 0, y = 0; y < ySize; y++, vi++)
        {
            for (int x = 0; x < xSize; x++, ti += 6, vi++)
            {
                triangles[ti] = vi;
                triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                triangles[ti + 4] = triangles[ti + 1] = vi + xSize + 1;
                triangles[ti + 5] = vi + xSize + 2;
            }
        }
        meshVis.triangles = triangles;

        meshVis.uv = uv;
        meshVis.tangents = tangents;

        meshVis.RecalculateNormals();
        meshVis.RecalculateBounds();
    }

    void OnDrawGizmos()
    {
        // Draw a green sphere at the transform's position and computed mesh
        Gizmos.color = Color.green;
        Gizmos.DrawWireMesh(meshVis, transform.position, transform.rotation, transform.lossyScale);
    }

    void OnDrawGizmosSelected()
    {
        // Draw a white sphere at the transform's position and computed mesh
        Gizmos.color = Color.white;
        Gizmos.DrawWireMesh(meshVis, transform.position, transform.rotation, transform.lossyScale);

        //if (meshLOD2!=null) Gizmos.DrawMesh(meshLOD2); //DEBUG
    }

    //Callback calls on script loaded and variables changes in editor
    private void OnValidate()
    {
        ValidateParams();
        GenerateVisMesh();
        GenerateVegPointsArray();
    }
#endif
    //End editor only staff
    //---------------------

    //Generate array of vegetation points (the point of minimal size field part, the center of the MinimalQuad) and it params
    private bool GenerateVegPointsArray()
    {
        //Size of vertices matrix (array.length), not world scale size in units
        int xSize = Mathf.FloorToInt(FieldLength / MinimalQuad);
        int ySize = Mathf.FloorToInt(FieldWidth / MinimalQuad);

        vegetationPoints = new VegetationPoint[(xSize) * (ySize)];

        if (vegetationPoints == null)
        {
            Debug.LogWarning("Can't create vegpoint array");
            return false;
        }

        //Compute the vegpoints Y coordinates (height under terrain or whatever it planted under)
        float MaxHeight = -1.0f;
        for (int i = 0, y = 0; y < ySize; y++)
        {
            for (int x = 0; x < xSize; x++, i++)
            {
                //Check height under terrain
                float zHeight = transform.position.y;
                RaycastHit hit;
                // Does the ray intersect any objects below
                Vector3 posRayCast = new Vector3(0, 0, 0);
                posRayCast = transform.TransformPoint(x * MinimalQuad + MinimalQuad * 0.5f, 1000.0f, y * MinimalQuad + MinimalQuad * 0.5f);
                if (Physics.Raycast(posRayCast, transform.TransformDirection(Vector3.down), out hit, Mathf.Infinity))
                {
                    posRayCast.y = posRayCast.y - hit.distance;
                    if (posRayCast.y > MaxHeight)
                        MaxHeight = posRayCast.y;
                }

                vegetationPoints[i].position = posRayCast;
                vegetationPoints[i].lodLevel = VegetationPoint.eVEGPOINTLOD.eVPHIGH;
                vegetationPoints[i].eType = VegetationPoint.eVEGPOINTTYPE.eVPWHOLE;
            }
        }


        //Calculate field bounds
        Vector3 FieldSize = new Vector3(FieldLength + MinimalQuad * 0.5f, MaxHeight, FieldWidth + MinimalQuad * 0.5f);
        Vector3 FieldCenter;
        FieldCenter.x = transform.position.x + FieldLength / 2.0f;
        FieldCenter.y = transform.position.y;
        FieldCenter.z = transform.position.z + FieldWidth / 2.0f;
        FieldBounds = new Bounds(FieldCenter, FieldSize);

        //Create LOD arrays (it's big, but fast)
        vegpointsLOD1 = new VegetationPoint[vegetationPoints.Length]; vpLOD1Q = 0;
        vegpointsLOD2 = new VegetationPoint[vegetationPoints.Length]; vpLOD2Q = 0;
        vegpointsLOD3 = new VegetationPoint[vegetationPoints.Length]; vpLOD3Q = 0;
        vegpointsLOD4 = new VegetationPoint[vegetationPoints.Length]; vpLOD4Q = 0;


        if ((vegpointsLOD1 == null) || (vegpointsLOD2 == null) || (vegpointsLOD3 == null) || (vegpointsLOD4 == null))
        {
            Debug.LogWarning("Can't create vegpoints LOD arrays");
            return false;
        }

        //Size of one plant
        PlantSize.x = WholePlant.Lod1.bounds.size.x * SizeCheat;
        PlantSize.y = WholePlant.Lod1.bounds.size.y * SizeCheat;

        //Create random density plant points at vegpoint (with mask if aviable)
        if (RandomShot == null)
        {
            int iDebugCounter = 0;
            Random.InitState(RandomSeed);
            RandomShot = new PlantPointToCB[Density];
            if ((RandomShot == null) || (RandomShot.Length == 0))
            {
                Debug.LogWarning("Can't create RandomShot array");
                return false;
            }
            for (int i = 0; i < RandomShot.Length; i++)
            {
                //Random size
                RandomShot[i].size.x = Random.Range(-SizeRandomization, SizeRandomization);//Random.Range(PlantSize.x - SizeRandomization, PlantSize.x + SizeRandomization);
                RandomShot[i].size.y = Random.Range(-SizeRandomization, SizeRandomization);//Random.Range(PlantSize.y - SizeRandomization, PlantSize.y + SizeRandomization);
                /*if (RandomShot[i].size.x < 0.0001f)
                    RandomShot[i].size.x = 0.0001f;
                if (RandomShot[i].size.y < 0.0001f)
                    RandomShot[i].size.y = 0.0001f;*/

                //Random color
                Color.RGBToHSV(ColorRandomization01, out float H01, out float S01, out float V01);
                Color.RGBToHSV(ColorRandomization02, out float H02, out float S02, out float V02);
                Color RandonColor = Random.ColorHSV(H01, H02, S01, S02, V01, V02);
                RandomShot[i].color = new Vector3(RandonColor.r, RandonColor.g, RandonColor.b);

                //Randomly plant plants with mask
                float randX = Random.Range(-MinimalQuad / 2.0f, MinimalQuad / 2.0f);
                float randY = Random.Range(-MinimalQuad / 2.0f, MinimalQuad / 2.0f);
                if (PlantMask != null)
                {                     
                    int x = Mathf.FloorToInt(randX / MinimalQuad * PlantMask.width);
                    int z = Mathf.FloorToInt(randY / MinimalQuad * PlantMask.height);
                    if (PlantMask.GetPixel(x, z).grayscale > 0.5f)
                    {
                        RandomShot[i].pos.x = randX;
                        RandomShot[i].pos.y = randY;
                        iDebugCounter = 0;
                    }
                    else
                        i--;
                }
                else
                {
                    RandomShot[i].pos.x = randX;
                    RandomShot[i].pos.y = randY;
                }
                iDebugCounter++;
                if (iDebugCounter > 64000)
                {
                    Debug.LogWarning("Can't plant RandomShot array with mask. Check if mask is not completely black.");
                    break;
                }
            }
        }

        bNeedLODComp = true;

        return true;
    }

    //Generate random plant distribution on current LOD
    private void GenerateRandomPlants(in VegetationPoint[] VegPointsLOD, in int LODQ, out PlantPointToCB[] points)
    {
        //Create plants array
        points = new PlantPointToCB[LODQ * Density];
        if ((points == null) || (points.Length == 0))
        {
            Debug.LogWarning("Can't create points array");
            return;
        }
        //Randomize plants into vegetation points
        int jj = 0;
        for (int i = 0; i < LODQ; i++)
        {
            for (int j = 0; j < Density; j++)
            {
                points[jj].pos.x = VegPointsLOD[i].position.x + RandomShot[j].pos.x;
                points[jj].pos.z = VegPointsLOD[i].position.z + RandomShot[j].pos.y;
                points[jj].pos.y = VegPointsLOD[i].position.y;

                points[jj].size.x = (PlantSize.x + RandomShot[j].size.x) * WholePlant.Lod1Scale.x;
                points[jj].size.y = (PlantSize.y + RandomShot[j].size.y) * WholePlant.Lod1Scale.y;

                points[jj].color = RandomShot[j].color;

                jj++;
            }
        }
    }

    //Generate computebuffers for LODs   
    //there VegPointsLOD - array of VegetationPoints of current LOD, LODMaterial - material of current LOD, 
    //LODQ - quantity of plants in LOD, current LOD compute buffer for shader
    private bool GeneratePlantsLODComputeBuffer(in VegetationPoint[] VegPointsLOD, ref Material LODMaterial, in int LODQ, out ComputeBuffer computebufferLOD)
    {
        computebufferLOD = null;
        if ((LODMaterial != null) && (LODQ > 0) && (VegPointsLOD != null) && (VegPointsLOD.Length>0))
        {
            //Randomize plants into vegetation points
            GenerateRandomPlants(VegPointsLOD, LODQ, out PlantPointToCB [] points);

            //Create compute buffer for shader
            if (computebufferLOD == null)
                computebufferLOD = new ComputeBuffer(points.Length, System.Runtime.InteropServices.Marshal.SizeOf<PlantPointToCB>());
            if (computebufferLOD == null)
            {
                Debug.LogWarning("Can't create computebuffer.");
                return false;
            }
            computebufferLOD.SetData(points);
            LODMaterial.SetBuffer("points", computebufferLOD);
        }
        return true;
    }

    //Check user input and modify to prevent errors, exceptions and unexpected behavior
    private void ValidateParams()
    {
        if ((WholePlant.Lod1 == null) || (WholePlant.Lod1Mat == null) || (WholePlant.Lod2 == null) || (WholePlant.Lod3 == null) || (WholePlant.Lod4 == null))
        {
            Debug.LogWarning("Whole plant is not initialized.");
            return;
        }
        if ((CuttedPlant.Lod1 == null) || (CuttedPlant.Lod1Mat == null) || (CuttedPlant.Lod2 == null) || (CuttedPlant.Lod3 == null) || (CuttedPlant.Lod4 == null))
        {
            Debug.LogWarning("Cutted Plant is not initialized.");
            return;
        }

        if (!WholePlant.Lod1Mat.enableInstancing)
        {
            Debug.LogWarning("Material " + WholePlant.Lod1Mat.name + " do not support GPU instancing.");
            return;
        }
        if (!CuttedPlant.Lod1Mat.enableInstancing)
        {
            Debug.LogWarning("Material " + CuttedPlant.Lod1Mat.name + " do not support GPU instancing.");
            return;
        }

        if (ObjectOfView == null)
        {
            Debug.LogWarning("ObjectOfView is not set.");
            return;
        }

        if (PlantMask != null)
        {
            if (PlantMask.isReadable != true)
            {
                Debug.LogWarning("PlantMask is not readable. Make it readeble in import settings dialog.");
                return;
            }
        }
        else
        {
            Debug.LogWarning("PlantMask was not set.");
        }
        
        if (FieldLength <= 0) FieldLength = 1;
        if (FieldWidth <= 0) FieldWidth = 1;

        if (SizeCheat <= 0) SizeCheat = 0.0000001f;

        if (Density <= 0) Density = 1;
    }

    // Start is called before the first frame update
    void Start()
    {
        IsReady = false;

        if (!SystemInfo.supportsInstancing)
        {
            Debug.LogWarning("GPU instancing is not supported.");
            return;
        }

        ValidateParams();

        if (!GenerateVegPointsArray())
            return;

        IsReady = true;
    }

    // Update is called once per frame
    void Update()
    {        
        //Editor only staff
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (transform.hasChanged)
            {
                if (RandomShot != null)
                    RandomShot = null;
                GenerateVisMesh();
                GenerateVegPointsArray();
                bNeedLODComp = true;
            }
            //DEBUG Visualization
            if (DebugMode)
            {
                if (vegetationPoints != null)
                    foreach (VegetationPoint vp in vegetationPoints)
                        Debug.DrawRay(vp.position, Vector3.up, Color.green, 1.5f);
            }
        }
        //End editor only staff
#endif

        //Something gone wrong while initialize
        if (!IsReady)
            return;

        //Check if it's necessary to recompute LODs
        if (ObjectOfView.hasChanged) bNeedLODComp = true;
        if (transform.hasChanged) bNeedLODComp = true;

        //Some unexpected bugs handling (if it is here)
        if ((vegetationPoints == null)||(vegetationPoints.Length<=0))
            GenerateVegPointsArray();
        if ((vegetationPoints == null) || (vegetationPoints.Length <= 0))
            return;

        //Сollate copies of veg point by lods
        if (bNeedLODComp)
        {
            //Check if arrays are valid (it must be already valid here anyway, so assert if not)
            if ((vegpointsLOD1 == null) || (vegpointsLOD2 == null) || (vegpointsLOD3 == null) || (vegpointsLOD4 == null))
            {
                Debug.Assert(true);
                return;
            }
                       
            //Clear buffers
            if (computebufferLOD2 != null)
                computebufferLOD2.Release();
            computebufferLOD2 = null;
            if (computebufferLOD3 != null)
                computebufferLOD3.Release();
            computebufferLOD3 = null;
            if (computebufferLOD4 != null)
                computebufferLOD4.Release();
            computebufferLOD4 = null;

            vpLOD1Q = 0;
            vpLOD2Q = 0;
            vpLOD3Q = 0;
            vpLOD4Q = 0;

            if (ObjectOfView == null)
                return;

            //Decompose of vegPoints to vepointLODs by distance
            for (int i = 0; i < vegetationPoints.Length; i++)
            {
                float fDistance = Vector3.Distance(vegetationPoints[i].position, ObjectOfView.position);
                if (fDistance < WholePlant.Lod1to2SwitchDistance)
                {
                    vegpointsLOD1[vpLOD1Q] = vegetationPoints[i];
                    vpLOD1Q++;
                }
                else
                   if (fDistance < WholePlant.Lod2to3SwitchDistance)
                    {
                        vegpointsLOD2[vpLOD2Q] = vegetationPoints[i];
                        vpLOD2Q++;
                    }
                    else
                       if (fDistance < WholePlant.Lod3to4SwitchDistance)
                        {
                            vegpointsLOD3[vpLOD3Q] = vegetationPoints[i];
                            vpLOD3Q++;
                        }
                        else
                        {
                            vegpointsLOD4[vpLOD4Q] = vegetationPoints[i];
                            vpLOD4Q++;
                        }
            }

            //TODO: Get size from LOD1 mesh and set that to billboards and LOD4 plane height and LOD4 quadsize = MinQuad
            //Create plant points by minimal quad density and compute geometry on shaders fully procedural, rendering at OnRenderObject()
            //LOD02 Render (X billbord, TODO shadows!)
            if (GeneratePlantsLODComputeBuffer(vegpointsLOD2, ref WholePlant.Lod2, vpLOD2Q, out computebufferLOD2) == false)
            {
                Debug.LogWarning("GeneratePlantsLOD2 fails.");
            }
            //LOD03Render (billboards without shadows)
            if (GeneratePlantsLODComputeBuffer(vegpointsLOD3, ref WholePlant.Lod3, vpLOD3Q, out computebufferLOD3) == false)
            {
                Debug.LogWarning("GeneratePlantsLOD3 fails.");
            }
            //LOD04Render (one plain mesh withoutshadows)
            if ((WholePlant.Lod4 != null)&&(vpLOD4Q > 0))
            {
                PlantPointToCB[] points = new PlantPointToCB[vpLOD4Q];
                if (points == null)
                {
                    Debug.LogWarning("GeneratePlantsLOD4 fails (points == null).");
                }
                for (int i = 0; i < vpLOD4Q; i++)
                {
                    points[i].pos = vegpointsLOD4[i].position;
                    points[i].size.x = MinimalQuad;
                    points[i].size.y = PlantSize.y;
                    points[i].color = new Vector3(ColorRandomization01.r, ColorRandomization01.g, ColorRandomization01.b);
                }
                if (computebufferLOD4 == null)
                    computebufferLOD4 = new ComputeBuffer(vpLOD4Q, System.Runtime.InteropServices.Marshal.SizeOf<PlantPointToCB>());
                if (computebufferLOD4 == null)
                {
                    Debug.LogWarning("GeneratePlantsLOD4 fails (computebufferLOD4 == null).");
                }
                computebufferLOD4.SetData(points);
                WholePlant.Lod4.SetBuffer("points", computebufferLOD4);
            }
            bNeedLODComp = false;
        }
        //LOD01Render (auto instancing mesh)
        if ((vpLOD1Q > 0) && (WholePlant.Lod1 != null) && (WholePlant.Lod1Mat != null))
        {
            //Randomize plants into vegetation points
            GenerateRandomPlants(vegpointsLOD1, vpLOD1Q, out PlantPointToCB[] pointsLOD1);
            int ii = 0;
            Material instMat = new Material(WholePlant.Lod1Mat); //if we create material here, instancing turns on 
            MaterialPropertyBlock materialPB = new MaterialPropertyBlock(); //To take effect of color changes SetColor() must be on PropertyBlock
            for (int ind = 0; ind < pointsLOD1.Length; ind++)
            {
                //Material instMat = new Material(WholePlant.Lod1Mat); //if we create material here, instancing turns off
                //instMat.SetColor("_ColorAlbedo", new Color(pointsLOD1[ind].color.x, pointsLOD1[ind].color.y, pointsLOD1[ind].color.z));
                materialPB.SetColor("_Color", new Color(pointsLOD1[ind].color.x, pointsLOD1[ind].color.y, pointsLOD1[ind].color.z)); //color for instance
                Matrix4x4 matrix = new Matrix4x4();
                matrix.SetTRS(pointsLOD1[ind].pos,
                        Quaternion.Euler(new Vector3(WholePlant.Lod1Rotation.x, WholePlant.Lod1Rotation.y * (pointsLOD1[ind].pos.x + pointsLOD1[ind].pos.y), WholePlant.Lod1Rotation.z)),
                                        new Vector3(WholePlant.Lod1Scale.y*(1 + RandomShot[ii].size.y), WholePlant.Lod1Scale.x*(1 + RandomShot[ii].size.x), WholePlant.Lod1Scale.y*(1 + RandomShot[ii].size.y) ) );//new Vector3(pointsLOD1[ind].size.y, pointsLOD1[ind].size.x, pointsLOD1[ind].size.y));//
                Graphics.DrawMesh(WholePlant.Lod1, matrix, instMat, 0, null, 0, materialPB);
                ii++;
                if (ii >= Density)
                    ii = 0;
            }
        }
    }

    void OnRenderObject()
    {
        //No shadows for LOD2, LOD3 and LOD4, render it fully creating geometry on shaders GPU
        if ((computebufferLOD2 != null) && (WholePlant.Lod2 != null))
        {
            WholePlant.Lod2.SetPass(0);
            Graphics.DrawProcedural(MeshTopology.Points, vpLOD2Q * Density, 0);
        }       
        if ((computebufferLOD3 != null)&&(WholePlant.Lod3 != null))
        {
            WholePlant.Lod3.SetPass(0);
            Graphics.DrawProcedural(MeshTopology.Points, vpLOD3Q * Density, 0);
        }
        if ((computebufferLOD4 != null)&&(WholePlant.Lod4 != null))
        {
            WholePlant.Lod4.SetPass(0);
            Graphics.DrawProcedural(MeshTopology.Points, vpLOD4Q * Density, 0);
        }        
    }

    void OnDestroy()
    {
        if (computebufferLOD2 != null)
            computebufferLOD2.Release();
        computebufferLOD2 = null;

        if (computebufferLOD3 != null)
            computebufferLOD3.Release();
        computebufferLOD3 = null;

        if (computebufferLOD4 != null)
            computebufferLOD4.Release();
        computebufferLOD4 = null;

        if (RandomShot != null)
            RandomShot = null;
    }
}

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class TerrainGenerator : MonoBehaviour
{
    public Material material;

    public int xSize = 200;
    public int zSize = 200;
    public enum TerrainType
    {
        Elevation,
        Terrace
    }
    public TerrainType terrainType;
    [Range(1f, 10f)]
    public float elevationExponent = 1;
    [Range(1, 32)]
    public int terraceCount = 10;
    public Vector2 offset = Vector2.zero;
    [Range(0f, 0.5f)]
    public float frequency = 0.02f;
    [Range(1, 10)]
    public int octaves = 1;
    public Vector2 heightRange = new Vector2(0, 1);
    public List<TerrainLevel> terrainLevels = new();
    [System.Serializable]
    public struct TerrainLevel
    {
        public float minValue;
        public Color color;
    }
    public List<WorldElement> worldElements = new();
    [System.Serializable]
    public struct WorldElement
    {
        public GameObject gameObject;
        public Vector2 spawnLevel;
        [Range(0f, 1f)]
        public float spawnFrequency;
        public bool rotateOnNormal;
    }
    private List<GameObject> _gameObjectsList = new();

    private Mesh _mesh;
    private int _currentXSize;
    private int _currentZSize;
    private Vector3[] _vertices;
    private int[] _triangles;
    private Color[] _colors;
    private float[] _vertexHeightsRaw;
    private bool[] _occupiedVertices;
    private bool _shouldRegenerateTerrain;

    void Awake()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if(meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();      

        if (_mesh == null)  
            _mesh = new Mesh();

        _mesh = meshFilter.mesh;
        _mesh.indexFormat = IndexFormat.UInt32; 

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
    
        meshRenderer.material = material;

        GenerateTerrain();
        UpdateTerrain();
    }

    void OnValidate()
    {
        if(_mesh == null)
            return;

        if(xSize < 1 || zSize < 1)
        {
            xSize = 1;
            zSize = 1;
            Debug.LogWarning("xSize and zSize must be greater than 0!");
        }

        _currentXSize = xSize;
        _currentZSize = zSize;

        if((uint)(xSize + 1) * (uint)(zSize +1) > uint.MaxValue)
        {
            Debug.LogWarning("Max vertex count exceeded!");
            xSize = _currentXSize;
            zSize = _currentZSize;
        }

        _shouldRegenerateTerrain = true;
    }

    void Update()
    {
        if(_shouldRegenerateTerrain)
        {
            GenerateTerrain();
            UpdateTerrain();

            _shouldRegenerateTerrain = false;
        }
    }

    void GenerateTerrain()
    {
        foreach(GameObject obj in _gameObjectsList)
        {
            Destroy(obj);
        }
        _gameObjectsList.Clear();

        _vertices = new Vector3[(xSize + 1) * (zSize + 1)];
        _vertexHeightsRaw = new float[_vertices.Length];
        _occupiedVertices = new bool[_vertices.Length];
        _colors = new Color[_vertices.Length];

        // Vertices
        for(int vertexIndex = 0, z = 0; z <= zSize; z++)
        {
            for(int x = 0; x <= xSize; x++)
            {
                float y = 0f;
                float octaveFrequency = frequency;
                for(int i = 0; i < octaves; i++)
                {
                    y += Mathf.PerlinNoise((x + offset.x) * octaveFrequency, (z + offset.y) * octaveFrequency);
                    octaveFrequency *= 2.0f;
                }
                y /= octaves;
                _vertexHeightsRaw[vertexIndex] = y;

                float yTerrain;
                if(terrainType == TerrainType.Terrace)
                    yTerrain = Mathf.Round(y * terraceCount) / terraceCount;
                else
                    yTerrain = Mathf.Pow(y, elevationExponent);

                float height = heightRange.x + (yTerrain * (heightRange.y - heightRange.x));
                _vertices[vertexIndex] = new Vector3(x, height, z);

                if(terrainLevels.Count == 0)
                    _colors[vertexIndex] = Color.magenta;
                else
                    _colors[vertexIndex] = GetColor(y);

                vertexIndex++;
            }
        }
        
        // Triangles
        _triangles = new int[xSize * zSize * 6];
        for(int vertex = 0, triangleIndex = 0, z = 0; z < zSize; z++)
        {
            for(int x = 0; x < xSize; x++)
            {
                _triangles[triangleIndex + 0] = vertex;
                _triangles[triangleIndex + 1] = vertex + xSize + 1;
                _triangles[triangleIndex + 2] = vertex + 1;

                _triangles[triangleIndex + 3] = vertex + 1;
                _triangles[triangleIndex + 4] = vertex + xSize + 1;
                _triangles[triangleIndex + 5] = vertex + xSize + 2;

                vertex++;
                triangleIndex += 6;
            }
            vertex++;
        }
    }

    void UpdateTerrain()
    {
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.triangles = _triangles;
        _mesh.SetColors(_colors);
        _mesh.RecalculateNormals();

        for(int vertexIndex = 0; vertexIndex < _vertices.Length; vertexIndex++)
        {
            foreach(WorldElement worldElement in worldElements)
            {
                if(_occupiedVertices[vertexIndex])
                    continue;

                if(_vertexHeightsRaw[vertexIndex] > worldElement.spawnLevel.x && _vertexHeightsRaw[vertexIndex] < worldElement.spawnLevel.y)
                {
                    float random = Random.Range(0f, 1f);
                    if(random < worldElement.spawnFrequency)
                    {
                        Quaternion rotation;
                        if(worldElement.rotateOnNormal)
                            rotation = Quaternion.FromToRotation(transform.up, _mesh.normals[vertexIndex]) * transform.rotation;
                        else
                            rotation = Quaternion.identity;

                        GameObject worldElementObject = Instantiate(worldElement.gameObject, _vertices[vertexIndex], rotation, transform);
                        _gameObjectsList.Add(worldElementObject);
                        _occupiedVertices[vertexIndex] = true;
                    }
                }
            } 
        }
    }

    Color GetColor(float height)
    {
        int closestHeightIndex = 0;
        float closestHeight = 0f;

        for(int i = 0; i < terrainLevels.Count; i++)
        {
            if(height > terrainLevels[i].minValue)
            {
                if(terrainLevels[i].minValue > closestHeight)
                {
                    closestHeightIndex = i;
                    closestHeight = terrainLevels[i].minValue;
                }
            }
        }

        return terrainLevels[closestHeightIndex].color;
    }
}

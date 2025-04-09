using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class TerrainGenerator : MonoBehaviour
{
    public Material Material;
    public int XSize = 500;
    public int ZSize = 500;
    public enum TerrainType
    {
        Elevation,
        Terrace
    }
    public TerrainType Type;
    [Range(1f, 10f)]
    public float ElevationExponent = 3;
    [Range(1, 32)]
    public int TerraceCount = 20;
    public Vector2 Offset = Vector2.zero;
    [Range(0f, 0.5f)]
    public float Frequency = 0.01f;
    [Range(1, 10)]
    public int Octaves = 2;
    public Vector2 HeightRange = new Vector2(0, 128);
    public List<TerrainLevel> Levels = new List<TerrainLevel>();
    [System.Serializable]
    public struct TerrainLevel
    {
        public float MinValue;
        public Color Color;
    }
    public List<WorldElement> WorldElements = new List<WorldElement>();
    [System.Serializable]
    public struct WorldElement
    {
        public GameObject GameObject;
        public Vector2 SpawnLevel;
        [Range(0f, 1f)]
        public float SpawnFrequency;
        public bool RotateOnNormal;
    }

    private int _currentXSize;
    private int _currentZSize;
    private bool _shouldRegenerateTerrain;
    private Mesh _mesh;
    private Vector3[] _vertices;
    private int[] _triangles;
    private Color[] _colors;
    private bool[] _occupiedVertices;
    private float[] _vertexHeightsRaw;
    private List<GameObject> _spawnablesList = new();

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
        meshRenderer.material = Material;

        UpdateTerrain();
    }

    void OnValidate()
    {
        if(_mesh == null)
            return;

        if(XSize < 1 || ZSize < 1)
        {
            XSize = 1;
            ZSize = 1;
            Debug.LogWarning("xSize and zSize must be greater than 0!");
        }

        _currentXSize = XSize;
        _currentZSize = ZSize;

        if((uint)(XSize + 1) * (uint)(ZSize +1) > uint.MaxValue)
        {
            Debug.LogWarning("Max vertex count exceeded!");
            XSize = _currentXSize;
            ZSize = _currentZSize;
        }

        _shouldRegenerateTerrain = true;
    }

    void Update()
    {
        if(_shouldRegenerateTerrain)
        {
            UpdateTerrain();
            _shouldRegenerateTerrain = false;
        }
    }

    public void UpdateTerrain()
    {
        CreateTerrainData();
        ApplyTerrainData();
    }

    private void CreateTerrainData()
    {
        foreach(GameObject obj in _spawnablesList)
        {
            Destroy(obj);
        }
        _spawnablesList.Clear();

        _vertices = new Vector3[(XSize + 1) * (ZSize + 1)];
        _vertexHeightsRaw = new float[_vertices.Length];     // Stores the height of each vertex before applying calculations according to the type of the terrain.
        _occupiedVertices = new bool[_vertices.Length];
        _colors = new Color[_vertices.Length];

        // Vertices
        for(int vertexIndex = 0, z = 0; z <= ZSize; z++)
        {
            for(int x = 0; x <= XSize; x++)
            {
                float y = 0f;
                float octaveFrequency = Frequency;
                for(int i = 0; i < Octaves; i++)
                {
                    y += Mathf.PerlinNoise((x + Offset.x) * octaveFrequency, (z + Offset.y) * octaveFrequency);
                    octaveFrequency *= 2.0f;
                }
                y /= Octaves;
                _vertexHeightsRaw[vertexIndex] = y;

                float yTerrain;
                if(Type == TerrainType.Terrace)
                    yTerrain = Mathf.Round(y * TerraceCount) / TerraceCount;
                else
                    yTerrain = Mathf.Pow(y, ElevationExponent);

                float height = HeightRange.x + (yTerrain * (HeightRange.y - HeightRange.x));
                _vertices[vertexIndex] = new Vector3(x, height, z);

                if(Levels.Count == 0)
                    _colors[vertexIndex] = Color.magenta;
                else
                    _colors[vertexIndex] = GetColor(y);

                vertexIndex++;
            }
        }
        
        // Triangles
        _triangles = new int[XSize * ZSize * 6];
        for(int vertex = 0, triangleIndex = 0, z = 0; z < ZSize; z++)
        {
            for(int x = 0; x < XSize; x++)
            {
                _triangles[triangleIndex + 0] = vertex;
                _triangles[triangleIndex + 1] = vertex + XSize + 1;
                _triangles[triangleIndex + 2] = vertex + 1;

                _triangles[triangleIndex + 3] = vertex + 1;
                _triangles[triangleIndex + 4] = vertex + XSize + 1;
                _triangles[triangleIndex + 5] = vertex + XSize + 2;

                vertex++;
                triangleIndex += 6;
            }
            vertex++;
        }
    }

    private Color GetColor(float height)
    {
        int closestHeightIndex = 0;
        float closestHeight = 0f;

        for(int i = 0; i < Levels.Count; i++)
        {
            if(height > Levels[i].MinValue)
            {
                if(Levels[i].MinValue > closestHeight)
                {
                    closestHeightIndex = i;
                    closestHeight = Levels[i].MinValue;
                }
            }
        }

        return Levels[closestHeightIndex].Color;
    }

    private void ApplyTerrainData()
    {
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.triangles = _triangles;
        _mesh.SetColors(_colors);
        _mesh.RecalculateNormals();

        SpawnWorldElements();
    }

    private void SpawnWorldElements()
    {
        if(WorldElements.Count == 0)
            return;

        for(int vertexIndex = 0; vertexIndex < _vertices.Length; vertexIndex++)
        {
            foreach(WorldElement worldElement in WorldElements)
            {
                if(_occupiedVertices[vertexIndex])
                    continue;

                if(_vertexHeightsRaw[vertexIndex] > worldElement.SpawnLevel.x && _vertexHeightsRaw[vertexIndex] < worldElement.SpawnLevel.y)
                {
                    float random = Random.Range(0f, 1f);
                    if(random < worldElement.SpawnFrequency)
                    {
                        Quaternion rotation;
                        if(worldElement.RotateOnNormal)
                            rotation = Quaternion.FromToRotation(transform.up, _mesh.normals[vertexIndex]) * transform.rotation;
                        else
                            rotation = Quaternion.identity;

                        GameObject worldElementObject = Instantiate(worldElement.GameObject, transform.position + _vertices[vertexIndex], transform.rotation * rotation, transform);
                        _spawnablesList.Add(worldElementObject);
                        _occupiedVertices[vertexIndex] = true;
                    }
                }
            }
        }
    }
}

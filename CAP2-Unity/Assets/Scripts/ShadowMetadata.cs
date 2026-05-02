using System;

namespace ShadowPrototype
{
    [Serializable]
    public class ShadowMetadata
    {
        public int n_vertices;
        public int n_triangles;
        public int n_boundary;
        public int[] boundary_indices;
        public float timestamp;
    }
}

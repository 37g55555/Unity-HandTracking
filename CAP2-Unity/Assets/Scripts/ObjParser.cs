using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShadowPrototype
{
    public static class ObjParser
    {
        public static Mesh Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.LogWarning("ObjParser could not find the requested file.");
                return null;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            try
            {
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("v ", StringComparison.Ordinal))
                    {
                        if (!TryParseVertex(line, out Vector3 vertex))
                        {
                            Debug.LogWarning("ObjParser skipped an invalid vertex line.");
                            continue;
                        }

                        vertices.Add(vertex);
                        continue;
                    }

                    if (line.StartsWith("f ", StringComparison.Ordinal))
                    {
                        if (!TryParseFace(line, out int a, out int b, out int c))
                        {
                            Debug.LogWarning("ObjParser only supports triangle faces in the current prototype.");
                            continue;
                        }

                        triangles.Add(a - 1);
                        triangles.Add(b - 1);
                        triangles.Add(c - 1);
                    }
                }
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"ObjParser could not read '{path}': {exception.Message}");
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning($"ObjParser could not access '{path}': {exception.Message}");
                return null;
            }

            if (vertices.Count == 0 || triangles.Count == 0)
            {
                Debug.LogWarning($"ObjParser found no valid mesh data in '{path}'.");
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool TryParseVertex(string line, out Vector3 vertex)
        {
            vertex = Vector3.zero;
            string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                return false;
            }

            if (!float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return false;
            }

            vertex = new Vector3(x, y, 0f);
            return true;
        }

        private static bool TryParseFace(string line, out int a, out int b, out int c)
        {
            a = 0;
            b = 0;
            c = 0;

            string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4)
            {
                return false;
            }

            return TryParseFaceIndex(tokens[1], out a) &&
                   TryParseFaceIndex(tokens[2], out b) &&
                   TryParseFaceIndex(tokens[3], out c);
        }

        private static bool TryParseFaceIndex(string token, out int index)
        {
            index = 0;
            string[] parts = token.Split('/');
            return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index > 0;
        }
    }
}

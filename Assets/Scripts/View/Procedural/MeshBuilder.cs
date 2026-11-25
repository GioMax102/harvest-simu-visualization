using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>Construye mallas simples por código (silos, tanques, árboles, plantas). Sin dependencias de escena.</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<Color> Colors = new();
        readonly List<List<int>> submeshes = new() { new List<int>() };
        public Matrix4x4 Transform = Matrix4x4.identity;
        public Color Color = Color.white;
        int submesh;

        public int VertexCount => Vertices.Count;
        public void SetSubmesh(int index)
        {
            while (submeshes.Count <= index) submeshes.Add(new List<int>());
            submesh = index;
        }

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Vertices.Add(Transform.MultiplyPoint3x4(position));
            Normals.Add(Transform.MultiplyVector(normal).normalized);
            Uvs.Add(uv);
            Colors.Add(Color);
            return Vertices.Count - 1;
        }

        public void AddTriangle(int a, int b, int c) { var list = submeshes[submesh]; list.Add(a); list.Add(b); list.Add(c); }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uvMin, Vector2 uvMax)
        {
            int i0 = AddVertex(a, normal, new Vector2(uvMin.x, uvMin.y));
            int i1 = AddVertex(b, normal, new Vector2(uvMin.x, uvMax.y));
            int i2 = AddVertex(c, normal, new Vector2(uvMax.x, uvMax.y));
            int i3 = AddVertex(d, normal, new Vector2(uvMax.x, uvMin.y));
            AddTriangle(i0, i1, i2); AddTriangle(i0, i2, i3);
        }

        /// <summary>Caja centrada en <paramref name="center"/>; las UV miden metros con <paramref name="uvScale"/>.</summary>
        public void AddBox(Vector3 center, Vector3 size, float uvScale = 1)
        {
            var h = size / 2;
            void Face(Vector3 n, Vector3 u, Vector3 v, float du, float dv)
            {
                var c = center + Vector3.Scale(n, h);
                AddQuad(c - u - v, c - u + v, c + u + v, c + u - v, n, Vector2.zero, new Vector2(du * uvScale, dv * uvScale));
            }
            Face(Vector3.up, new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), size.x, size.z);
            Face(Vector3.down, new Vector3(0, 0, h.z), new Vector3(h.x, 0, 0), size.z, size.x);
            Face(Vector3.forward, new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), size.y, size.x);
            Face(Vector3.back, new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), size.x, size.y);
            Face(Vector3.right, new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), size.z, size.y);
            Face(Vector3.left, new Vector3(0, h.y, 0), new Vector3(0, 0, h.z), size.y, size.z);
        }

        /// <summary>Cilindro (o tronco de cono) vertical desde <paramref name="baseCenter"/>; U da la vuelta y V sube en metros.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, bool capTop = true, bool capBottom = false,
            float topRadius = -1, float uTiles = 1)
        {
            if (topRadius < 0) topRadius = radius;
            float slope = (radius - topRadius) / Mathf.Max(height, 1e-4f);
            int start = VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var normal = (dir + Vector3.up * slope).normalized;
                AddVertex(baseCenter + dir * radius, normal, new Vector2(uTiles * i / segments, 0));
                AddVertex(baseCenter + dir * topRadius + Vector3.up * height, normal, new Vector2(uTiles * i / segments, height));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                AddTriangle(a, a + 3, a + 1); AddTriangle(a, a + 2, a + 3);
            }
            if (capTop && topRadius > 0) AddDisc(baseCenter + Vector3.up * height, topRadius, segments, Vector3.up);
            if (capBottom && radius > 0) AddDisc(baseCenter, radius, segments, Vector3.down);
        }

        public void AddDisc(Vector3 center, float radius, int segments, Vector3 normal)
        {
            int c = AddVertex(center, normal, new Vector2(.5f, .5f));
            int first = VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                AddVertex(center + dir * radius, normal, new Vector2(.5f + dir.x * .5f, .5f + dir.z * .5f));
            }
            for (int i = 0; i < segments; i++)
            {
                if (normal.y > 0) AddTriangle(c, first + i, first + i + 1);
                else AddTriangle(c, first + i + 1, first + i);
            }
        }

        /// <summary>Cono con la base en <paramref name="baseCenter"/>.</summary>
        public void AddCone(Vector3 baseCenter, float radius, float height, int segments, float uTiles = 1)
        {
            int start = VertexCount;
            float slant = Mathf.Sqrt(radius * radius + height * height);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var normal = (dir * height / slant + Vector3.up * radius / slant).normalized;
                AddVertex(baseCenter + dir * radius, normal, new Vector2(uTiles * i / segments, 0));
                AddVertex(baseCenter + Vector3.up * height, normal, new Vector2(uTiles * (i + .5f) / segments, slant));
            }
            for (int i = 0; i < segments; i++) { int a = start + i * 2; AddTriangle(a, a + 2, a + 1); }
        }

        /// <summary>Esfera irregular (copa de árbol, roca): icosaedro subdividido con ruido radial.</summary>
        public void AddBlob(Vector3 center, Vector3 radii, int subdivisions, float roughness, int seed, bool flatShaded = false)
        {
            var (verts, tris) = IcoSphere(subdivisions);
            var random = new System.Random(seed);
            float ox = (float)random.NextDouble() * 100, oy = (float)random.NextDouble() * 100;
            var displaced = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                float n = Mathf.PerlinNoise(v.x * 1.7f + ox, v.z * 1.7f + oy) * .7f + Mathf.PerlinNoise(v.y * 3.1f + oy, v.x * 3.1f + ox) * .3f;
                displaced[i] = Vector3.Scale(v * (1 + roughness * (n - .5f) * 2), radii);
            }
            if (flatShaded)
            {
                for (int i = 0; i < tris.Count; i += 3)
                {
                    var a = displaced[tris[i]]; var b = displaced[tris[i + 1]]; var c = displaced[tris[i + 2]];
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    int i0 = AddVertex(center + a, n, new Vector2(a.x, a.z));
                    int i1 = AddVertex(center + b, n, new Vector2(b.x, b.z));
                    int i2 = AddVertex(center + c, n, new Vector2(c.x, c.z));
                    AddTriangle(i0, i1, i2);
                }
                return;
            }
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                var n = Vector3.Cross(displaced[tris[i + 1]] - displaced[tris[i]], displaced[tris[i + 2]] - displaced[tris[i]]);
                normals[tris[i]] += n; normals[tris[i + 1]] += n; normals[tris[i + 2]] += n;
            }
            int start = VertexCount;
            for (int i = 0; i < verts.Count; i++)
            {
                // Copas de árbol: las normales se inclinan hacia arriba para un sombreado más suave de follaje.
                var n = (normals[i].normalized + verts[i] * .5f).normalized;
                AddVertex(center + displaced[i], n, new Vector2(verts[i].x * .5f + .5f, verts[i].y * .5f + .5f));
            }
            for (int i = 0; i < tris.Count; i += 3) AddTriangle(start + tris[i], start + tris[i + 1], start + tris[i + 2]);
        }

        static readonly Dictionary<int, (List<Vector3>, List<int>)> icoCache = new();
        static (List<Vector3>, List<int>) IcoSphere(int subdivisions)
        {
            if (icoCache.TryGetValue(subdivisions, out var cached)) return cached;
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int index)) return index;
                    verts.Add(((verts[a] + verts[b]) / 2).normalized);
                    return mid[key] = verts.Count - 1;
                }
                var next = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            // Unity toma como frente el giro horario: la normal Cross(b-a, c-a) debe apuntar hacia afuera.
            for (int i = 0; i < tris.Count; i += 3)
            {
                var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }
            return icoCache[subdivisions] = (verts, tris);
        }

        public Mesh ToMesh(string name, bool withColors = false)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            if (withColors) mesh.SetColors(Colors);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}

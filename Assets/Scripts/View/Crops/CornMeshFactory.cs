using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Plantas de maíz seco (listo para cosecha) generadas por código. Cada malla es un tramo de hilera a lo largo de X local,
    /// centrado en el origen. Los colores salen de la paleta (<see cref="ProcTextures.CornPalette"/>) mediante UV.
    /// Es el respaldo mientras no haya un modelo de planta; se reemplaza asignando otras mallas al CornField.
    /// </summary>
    public static class CornMeshFactory
    {
        public const float SegmentLength = 1.5f;
        public const int PlantsPerSegment = 3;
        public const int ToneBuckets = 3, VariantsPerBucket = 4, Lods = 2;

        /// <summary>Mallas indexadas por [lod, tono, variante]. Tono 0 claro (poco rendimiento) a 2 oscuro (mucho).</summary>
        public static Mesh[,,] BuildSet(int seed = 7)
        {
            var set = new Mesh[Lods, ToneBuckets, VariantsPerBucket];
            for (int lod = 0; lod < Lods; lod++)
                for (int tone = 0; tone < ToneBuckets; tone++)
                    for (int variant = 0; variant < VariantsPerBucket; variant++)
                        set[lod, tone, variant] = Segment(lod, tone, new System.Random(seed + tone * 31 + variant * 7));
            return set;
        }

        public static Mesh Segment(int lod, int tone, System.Random random)
        {
            var builder = new MeshBuilder();
            for (int i = 0; i < PlantsPerSegment; i++)
            {
                float x = -SegmentLength / 2 + (i + .5f) * SegmentLength / PlantsPerSegment + Range(random, -.09f, .09f);
                float z = Range(random, -.05f, .05f);
                float height = Range(random, 2.05f, 2.55f) * (1 + tone * .04f);
                int column = Mathf.Clamp(tone * 3 - 1 + random.Next(0, 3), 0, ProcTextures.PaletteColumns - 1);
                builder.Transform = Matrix4x4.TRS(new Vector3(x, 0, z), Quaternion.Euler(0, Range(random, 0, 360), 0), Vector3.one);
                if (lod == 0) Plant(builder, height, column, random); else SimplePlant(builder, height, column, random);
            }
            builder.Transform = Matrix4x4.identity;
            var mesh = builder.ToMesh($"Maiz_L{lod}_T{tone}");
            // Margen para que el corte por frustum no recorte hojas que salen del tramo.
            var b = mesh.bounds; b.Expand(new Vector3(.8f, .2f, .8f)); mesh.bounds = b;
            return mesh;
        }

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        static void Plant(MeshBuilder b, float height, int column, System.Random random)
        {
            // Tallo: prisma de 4 caras que se adelgaza; la base va más oscura (oclusión falsa por UV).
            Stalk(b, height, .02f, .011f, 4, column);
            int leaves = 7;
            float side = Range(random, 0, 360);
            for (int i = 0; i < leaves; i++)
            {
                float y = Mathf.Lerp(.45f, height - .45f, i / (float)(leaves - 1)) + Range(random, -.06f, .06f);
                side += 180 + Range(random, -35, 35);
                float length = Mathf.Lerp(.95f, .55f, i / (float)leaves) * Range(random, .85f, 1.1f);
                Leaf(b, y, side, length, .075f, 3, Range(random, .55f, 1.05f), column);
            }
            // Mazorca colgando hacia afuera, envuelta en totomoxtle seco.
            float earAngle = side + 90;
            var dir = Quaternion.Euler(0, earAngle, 0) * Quaternion.Euler(Range(random, 110, 150), 0, 0) * Vector3.up;
            Ear(b, new Vector3(0, height * .48f, 0), dir, .24f, .033f, column);
            // Espiga: tres varas finas en la punta.
            for (int i = 0; i < 3; i++)
            {
                var top = new Vector3(0, height, 0);
                var tip = top + Quaternion.Euler(0, Range(random, 0, 360), 0) * new Vector3(.08f, .28f, 0);
                Blade(b, top, tip, .012f, ProcTextures.PaletteUv(column, ProcTextures.CornPart.Tassel));
            }
        }

        static void SimplePlant(MeshBuilder b, float height, int column, System.Random random)
        {
            Stalk(b, height, .022f, .012f, 3, column);
            float side = Range(random, 0, 360);
            // Lejos se ven menos hojas pero más anchas, para cubrir el suelo como el dosel real.
            for (int i = 0; i < 5; i++)
            {
                float y = Mathf.Lerp(.6f, height - .4f, i / 4f);
                side += 144 + Range(random, -30, 30);
                Leaf(b, y, side, Range(random, .7f, .95f), .16f, 1, Range(random, .5f, .9f), column);
            }
        }

        static void Stalk(MeshBuilder b, float height, float bottom, float top, int sides, int column)
        {
            int start = b.VertexCount;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                b.AddVertex(dir * bottom, dir, ProcTextures.PaletteUv(column, ProcTextures.CornPart.StalkBottom));
                b.AddVertex(dir * top + Vector3.up * height, dir, ProcTextures.PaletteUv(column, ProcTextures.CornPart.StalkTop));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2;
                b.AddTriangle(a, a + 3, a + 1); b.AddTriangle(a, a + 2, a + 3);
            }
        }

        /// <summary>Hoja que sale del tallo, sube y se dobla hacia abajo (hojas secas). Se dibuja por ambas caras.</summary>
        static void Leaf(MeshBuilder b, float y, float azimuth, float length, float width, int segments, float droop, int column)
        {
            var rotation = Quaternion.Euler(0, azimuth, 0);
            var outward = rotation * Vector3.forward;
            var across = rotation * Vector3.right;
            var position = new Vector3(0, y, 0);
            var direction = (outward + Vector3.up * .9f).normalized;
            int previousLeft = -1, previousRight = -1;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                float w = width * Mathf.Sin(Mathf.Lerp(.35f, 3.0f, t)) + .004f;
                // Normal hacia arriba: con hojas tan delgadas el follaje se ilumina mejor como "dosel".
                var normal = (Vector3.up * .75f + Vector3.Cross(direction, across).normalized * .25f).normalized;
                var uv = ProcTextures.PaletteUv(column, t < .5f ? ProcTextures.CornPart.LeafBase : ProcTextures.CornPart.LeafTip, t < .5f ? t : t - .5f);
                int left = b.AddVertex(position - across * w, normal, uv);
                int right = b.AddVertex(position + across * w, normal, uv);
                if (previousLeft >= 0)
                {
                    b.AddTriangle(previousLeft, left, right); b.AddTriangle(previousLeft, right, previousRight);
                }
                previousLeft = left; previousRight = right;
                float step = length / segments;
                position += direction * step;
                direction = Vector3.Slerp(direction, (outward - Vector3.up * .6f).normalized, droop / segments * 1.3f).normalized;
            }
        }

        static void Ear(MeshBuilder b, Vector3 origin, Vector3 direction, float length, float radius, int column)
        {
            var rotation = Quaternion.FromToRotation(Vector3.up, direction);
            const int sides = 5;
            int start = b.VertexCount;
            var uv = ProcTextures.PaletteUv(column, ProcTextures.CornPart.Husk);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                var radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                b.AddVertex(origin + rotation * (radial * radius * .7f), rotation * radial, uv);
                b.AddVertex(origin + rotation * (radial * radius + Vector3.up * length * .55f), rotation * radial, uv);
                b.AddVertex(origin + rotation * (radial * radius * .2f + Vector3.up * length), rotation * (radial + Vector3.up).normalized, uv);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 3, n = a + 3;
                b.AddTriangle(a, n + 1, a + 1); b.AddTriangle(a, n, n + 1);
                b.AddTriangle(a + 1, n + 2, a + 2); b.AddTriangle(a + 1, n + 1, n + 2);
            }
        }

        static void Blade(MeshBuilder b, Vector3 from, Vector3 to, float width, Vector2 uv)
        {
            var dir = (to - from).normalized;
            var side = Vector3.Cross(dir, Vector3.forward).normalized * width;
            if (side.sqrMagnitude < 1e-8f) side = Vector3.right * width;
            var normal = Vector3.up;
            int i0 = b.AddVertex(from - side, normal, uv);
            int i1 = b.AddVertex(from + side, normal, uv);
            int i2 = b.AddVertex(to, normal, uv);
            b.AddTriangle(i0, i2, i1);
        }

        /// <summary>Rastrojo: tallos cortados a unos 30 cm, para celdas cosechadas vistas de cerca.</summary>
        public static Mesh Stubble(int seed = 3)
        {
            var random = new System.Random(seed);
            var b = new MeshBuilder();
            for (int i = 0; i < PlantsPerSegment; i++)
            {
                float x = -SegmentLength / 2 + (i + .5f) * SegmentLength / PlantsPerSegment + Range(random, -.09f, .09f);
                b.Transform = Matrix4x4.TRS(new Vector3(x, 0, Range(random, -.05f, .05f)),
                    Quaternion.Euler(Range(random, -12, 12), Range(random, 0, 360), Range(random, -12, 12)), Vector3.one);
                int column = random.Next(1, 6);
                int start = b.VertexCount;
                var uvBottom = ProcTextures.PaletteUv(column, ProcTextures.CornPart.StalkBottom);
                var uvTop = ProcTextures.PaletteUv(column, ProcTextures.CornPart.Stubble);
                float h = Range(random, .2f, .35f);
                for (int s = 0; s <= 3; s++)
                {
                    float a = s * Mathf.PI * 2 / 3;
                    var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    b.AddVertex(dir * .022f, dir, uvBottom);
                    b.AddVertex(dir * .02f + Vector3.up * h, dir, uvTop);
                }
                for (int s = 0; s < 3; s++) { int a = start + s * 2; b.AddTriangle(a, a + 3, a + 1); b.AddTriangle(a, a + 2, a + 3); }
            }
            b.Transform = Matrix4x4.identity;
            return b.ToMesh("Rastrojo");
        }
    }
}

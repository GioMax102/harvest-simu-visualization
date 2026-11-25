using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Estructuras y paisaje alrededor de la cuadrícula: silos junto a la bodega, gasolinera por base, obstáculos
    /// según su forma (seto o arboleda), cerca y árboles en el margen. Todo es presentación y se genera por código;
    /// las mallas se agrupan por material para dibujar con pocas llamadas.
    /// </summary>
    public sealed class SceneryBuilder
    {
        public const float SiloRadius = 4.2f, SiloHeight = 11f;
        readonly List<Mesh> meshes = new();
        readonly List<GameObject> objects = new();
        FieldGrid grid;
        HarvestArt art;
        System.Random random;

        // Una malla por material.
        MeshBuilder steel, painted, bark, foliage, rock, wood, concrete;

        public IReadOnlyList<GameObject> Objects => objects;

        public void Build(FieldGrid layout, HarvestArt artSet, Transform parent, int seed = 11)
        {
            grid = layout; art = artSet; random = new System.Random(seed);
            steel = new MeshBuilder(); painted = new MeshBuilder(); bark = new MeshBuilder(); foliage = new MeshBuilder();
            rock = new MeshBuilder(); wood = new MeshBuilder(); concrete = new MeshBuilder();

            SiloComplex(grid.CellToWorld(grid.Warehouse.x, grid.Warehouse.y));
            foreach (var home in grid.Bases) FuelStation(grid.CellToWorld(home.Value.x, home.Value.y));
            foreach (var group in grid.ObstacleGroups)
                if (group.IsLinear) Hedgerow(group); else Grove(group);
            Fence();
            MarginTrees();

            Emit("Silos y estructuras", steel, art.steelMaterial, ShadowCastingMode.On, parent);
            Emit("Pintura", painted, art.paintedMetalMaterial, ShadowCastingMode.On, parent);
            Emit("Troncos", bark, art.barkMaterial, ShadowCastingMode.On, parent);
            Emit("Follaje", foliage, art.foliageMaterial, ShadowCastingMode.On, parent);
            Emit("Rocas", rock, art.rockMaterial, ShadowCastingMode.On, parent);
            Emit("Cerca", wood, art.woodMaterial, ShadowCastingMode.On, parent);
            Emit("Patios", concrete, art.concreteMaterial, ShadowCastingMode.Off, parent);
        }

        void Emit(string name, MeshBuilder builder, Material material, ShadowCastingMode shadows, Transform parent)
        {
            if (builder.VertexCount == 0 || !material) return;
            var mesh = builder.ToMesh(name);
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            objects.Add(go);
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        /// <summary>Silos de lámina corrugada al noroeste de la bodega, pierna elevadora y tolva de recepción en la celda.</summary>
        void SiloComplex(Vector3 warehouse)
        {
            float half = grid.CellSize / 2;
            // Tolva de recepción (rejilla) sobre la celda de la bodega.
            steel.AddBox(warehouse + new Vector3(0, .12f, 0), new Vector3(3.2f, .24f, 3.2f));
            var siloCenters = new[]
            {
                warehouse + new Vector3(-7, 0, half + 9), warehouse + new Vector3(-17, 0, half + 9),
                warehouse + new Vector3(-7, 0, half + 19), warehouse + new Vector3(-17, 0, half + 19)
            };
            float circumference = 2 * Mathf.PI * SiloRadius;
            foreach (var c in siloCenters)
            {
                concrete.AddCylinder(c, SiloRadius + .6f, .3f, 32, true, false, -1, 4);
                steel.AddCylinder(c + Vector3.up * .3f, SiloRadius, SiloHeight, 40, false, false, -1, circumference / .14f);
                steel.AddCone(c + Vector3.up * (SiloHeight + .3f), SiloRadius + .15f, 3.2f, 40, 40);
                steel.AddCylinder(c + Vector3.up * (SiloHeight + 3.4f), .5f, .5f, 12);
            }
            // Pierna elevadora: torre delgada con cabeza, y ductos inclinados hacia cada silo.
            var leg = warehouse + new Vector3(-2, 0, half + 2.5f);
            float legHeight = SiloHeight + 9;
            painted.AddBox(leg + Vector3.up * legHeight / 2, new Vector3(1.2f, legHeight, 1.6f));
            painted.AddBox(leg + Vector3.up * (legHeight + .6f), new Vector3(2f, 1.2f, 2.4f));
            foreach (var c in siloCenters)
                Pipe(steel, leg + Vector3.up * (legHeight - .3f), c + Vector3.up * (SiloHeight + 3.6f), .18f);
            // Ducto de la tolva de recepción al pie de la pierna.
            Pipe(steel, warehouse + new Vector3(0, .3f, 1.4f), leg + Vector3.up * .8f, .25f);
        }

        void Pipe(MeshBuilder builder, Vector3 from, Vector3 to, float radius)
        {
            var dir = to - from;
            var previous = builder.Transform;
            builder.Transform = previous * Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.up, dir.normalized), Vector3.one);
            builder.AddCylinder(Vector3.zero, radius, dir.magnitude, 8, false);
            builder.Transform = previous;
        }

        /// <summary>Gasolinera de la base: patio, tanque horizontal sobre soportes, bomba y tambos. Va al norte de la celda.</summary>
        void FuelStation(Vector3 home)
        {
            float half = grid.CellSize / 2;
            var pad = home + new Vector3(0, 0, half + 5.5f);
            concrete.AddBox(pad + Vector3.up * .1f, new Vector3(grid.CellSize * .8f, .2f, 8f), 1);
            var previous = painted.Transform;
            painted.Transform = Matrix4x4.TRS(pad + new Vector3(-1.5f, 1.7f, 1.2f), Quaternion.Euler(0, 0, 90), Vector3.one);
            painted.AddCylinder(new Vector3(0, -2.2f, 0), 1.1f, 4.4f, 20, true, true);
            painted.Transform = previous;
            steel.AddBox(pad + new Vector3(-3, .6f, 1.2f), new Vector3(.25f, 1.1f, 2f));
            steel.AddBox(pad + new Vector3(0, .6f, 1.2f), new Vector3(.25f, 1.1f, 2f));
            painted.AddBox(pad + new Vector3(2.8f, .95f, -1.5f), new Vector3(.7f, 1.7f, .55f));
            for (int i = 0; i < 2; i++) painted.AddCylinder(pad + new Vector3(3.6f + i * .7f, .2f, 1.8f), .3f, .9f, 12);
        }

        /// <summary>Árbol de copa irregular. La copa usa varias esferas deformadas.</summary>
        void Tree(Vector3 basePosition, float height, int seed)
        {
            var r = new System.Random(seed);
            float trunkHeight = height * .45f;
            bark.AddCylinder(basePosition, height * .035f, trunkHeight + height * .1f, 7, false, false, height * .02f, 2);
            int blobs = 3 + r.Next(0, 3);
            float crown = height * .32f;
            for (int i = 0; i < blobs; i++)
            {
                var offset = new Vector3((float)(r.NextDouble() - .5) * crown, (float)r.NextDouble() * crown * .7f, (float)(r.NextDouble() - .5) * crown);
                var radii = new Vector3(crown, crown * .85f, crown) * (float)(.6 + r.NextDouble() * .35);
                foliage.AddBlob(basePosition + Vector3.up * (trunkHeight + crown * .55f) + offset, radii, 2, .35f, seed * 13 + i);
            }
        }

        void Shrub(Vector3 position, float size, int seed) =>
            foliage.AddBlob(position + Vector3.up * size * .35f, new Vector3(size, size * .6f, size), 1, .4f, seed);

        void Boulder(Vector3 position, float size, int seed) =>
            rock.AddBlob(position + Vector3.up * size * .2f, new Vector3(size, size * .55f, size * .8f), 1, .5f, seed, true);

        /// <summary>Obstáculo lineal (por ejemplo el muro de E): línea de árboles y arbustos.</summary>
        void Hedgerow(ObstacleGroup group)
        {
            float size = grid.CellSize;
            foreach (var cell in group.Cells)
            {
                var c = grid.CellToWorld(cell.x, cell.y);
                for (int i = 0; i < 2; i++)
                    Tree(c + new Vector3(Range(-size * .3f, size * .3f), 0, Range(-size * .3f, size * .3f)), Range(8, 13), random.Next());
                for (int i = 0; i < 5; i++)
                    Shrub(c + new Vector3(Range(-size * .45f, size * .45f), 0, Range(-size * .45f, size * .45f)), Range(1.6f, 3f), random.Next());
            }
        }

        /// <summary>Obstáculo en bloque: arboleda con rocas.</summary>
        void Grove(ObstacleGroup group)
        {
            float size = grid.CellSize;
            foreach (var cell in group.Cells)
            {
                var c = grid.CellToWorld(cell.x, cell.y);
                for (int i = 0; i < 3; i++)
                    Tree(c + new Vector3(Range(-size * .32f, size * .32f), 0, Range(-size * .32f, size * .32f)), Range(9, 15), random.Next());
                for (int i = 0; i < 3; i++)
                    Boulder(c + new Vector3(Range(-size * .4f, size * .4f), 0, Range(-size * .4f, size * .4f)), Range(.8f, 2.2f), random.Next());
                for (int i = 0; i < 3; i++)
                    Shrub(c + new Vector3(Range(-size * .45f, size * .45f), 0, Range(-size * .45f, size * .45f)), Range(1.2f, 2.4f), random.Next());
            }
        }

        /// <summary>Cerca de postes y alambre en los lados O, S y E; el lado norte es el camino de servicio.</summary>
        void Fence()
        {
            var b = grid.WorldBounds;
            float offset = 2.5f, spacing = 4f;
            var corners = new[]
            {
                new Vector3(b.min.x - offset, 0, b.max.z), new Vector3(b.min.x - offset, 0, b.min.z - offset),
                new Vector3(b.max.x + offset, 0, b.min.z - offset), new Vector3(b.max.x + offset, 0, b.max.z)
            };
            for (int side = 0; side < 3; side++)
            {
                var from = corners[side]; var to = corners[side + 1];
                int posts = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / spacing));
                for (int i = 0; i <= posts; i++)
                {
                    var p = Vector3.Lerp(from, to, i / (float)posts);
                    wood.AddCylinder(p, .07f, 1.35f, 6, true, false, .06f);
                }
                foreach (float h in new[] { .55f, .9f, 1.25f })
                    Pipe(wood, from + Vector3.up * h, to + Vector3.up * h, .012f);
            }
        }

        /// <summary>Árboles sueltos fuera de la cerca, lejos del camino de servicio y de los silos.</summary>
        void MarginTrees()
        {
            var b = grid.WorldBounds;
            float inner = grid.CellSize * 1.3f, outer = grid.CellSize * Mathf.Max(3, art.marginCells);
            int count = Mathf.RoundToInt((b.size.x + b.size.z) / 14f);
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    var p = new Vector3(Range(b.min.x - outer, b.max.x + outer), 0, Range(b.min.z - outer, b.max.z + outer * .3f));
                    bool insideRing = p.x > b.min.x - inner && p.x < b.max.x + inner && p.z > b.min.z - inner;
                    if (insideRing) continue;
                    Tree(p, Range(8, 16), random.Next());
                    if (random.NextDouble() < .5) Shrub(p + new Vector3(Range(-4, 4), 0, Range(-4, 4)), Range(1.5f, 3f), random.Next());
                    break;
                }
            }
        }

        public void Destroy()
        {
            foreach (var go in objects) if (go) DestroyObject(go);
            foreach (var mesh in meshes) DestroyObject(mesh);
            objects.Clear(); meshes.Clear();
        }

        static void DestroyObject(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }
    }
}

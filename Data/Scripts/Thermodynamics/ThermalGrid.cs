using ProtoBuf.Meta;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Voxels;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Transactions;
using System.Xml;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Thermodynamics
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public partial class ThermalGrid : MyGameLogicComponent
    {

        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

        public MyCubeGrid Grid;
        public Dictionary<int, int> PositionToIndex;
        public MyFreeList<ThermalCell> Thermals;
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();
        public ThermalRadiationNode SolarRadiationNode;
        private MyFreeList<ThermalRadiationNode> RadiationNodes;

        private int IterationFrames = 0;
        private int IterationIndex = 0;
        private int CountPerFrame = 0;
        public bool ThermalCellUpdateComplete = true;

        public Vector3 FrameSolarDirection;
        public MatrixD FrameMatrix;
        public Vector3D FrameGridCenter;
        public float FrameAmbientTemprature;
        public float FrameAmbientStrength;
        public float FrameSolarDecay;
        public bool FrameSolarOccluded;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Grid = Entity as MyCubeGrid;

            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent();

            PositionToIndex = new Dictionary<int, int>();
            Thermals = new MyFreeList<ThermalCell>();

            if (RadiationNodes == null)
            {
                RadiationNodes = new MyFreeList<ThermalRadiationNode>();
            }

            SolarRadiationNode = new ThermalRadiationNode();

            Grid.OnGridSplit += GridSplit;
            Grid.OnGridMerge += GridMerge;

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            //MyLog.Default.Info($"[{Settings.Name}] {Entity.DisplayName} ({Entity.EntityId}) Storage is empty: {Entity.Storage == null}");

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            Save();
            return base.IsSerialized();
        }

        private string Pack()
        {
            byte[] bytes = new byte[Thermals.Count * 8];

            int bi = 0;
            for (int i = 0; i < Thermals.UsedLength; i++)
            {
                ThermalCell c = Thermals.list[i];
                if (c == null) continue;

                int id = c.Id;
                bytes[bi] = (byte)id;
                bytes[bi + 1] = (byte)(id >> 8);
                bytes[bi + 2] = (byte)(id >> 16);
                bytes[bi + 3] = (byte)(id >> 24);

                int t = (int)(c.Temperature * 10000);
                bytes[bi + 4] = (byte)t;
                bytes[bi + 5] = (byte)(t >> 8);
                bytes[bi + 6] = (byte)(t >> 16);
                bytes[bi + 7] = (byte)(t >> 24);

                bi += 8;
            }

            return Convert.ToBase64String(bytes);
        }

        private void Unpack(string data)
        {
            byte[] bytes = Convert.FromBase64String(data);

            for (int i = 0; i < bytes.Length; i += 8)
            {
                int id = bytes[i];
                id |= bytes[i + 1] << 8;
                id |= bytes[i + 2] << 16;
                id |= bytes[i + 3] << 24;

                int f = bytes[i + 4];
                f |= bytes[i + 5] << 8;
                f |= bytes[i + 6] << 16;
                f |= bytes[i + 7] << 24;

                Thermals.list[PositionToIndex[id]].Temperature = f * 0.0001f;
            }
        }

        private void Save()
        {
            Stopwatch sw = Stopwatch.StartNew();

            string data = Pack();

            //string data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(PackGridInfo()));

            MyModStorageComponentBase storage = Entity.Storage;
            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = data;
            }
            else
            {
                storage.Add(StorageGuid, data);
            }
            sw.Stop();
            MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        private void Load()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (Entity.Storage.ContainsKey(StorageGuid))
                {
                    Unpack(Entity.Storage[StorageGuid]);
                }
            }
            catch
            {

            }

            sw.Stop();
            MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }

        private void BlockAdded(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            ThermalCell cell = new ThermalCell(this, b);

            cell.AssignNeighbors();

            //MyLog.Default.Info($"[{Settings.Name}] Added {Grid.EntityId} ({b.Position.Flatten()}) {b.Position} --- {type}/{subtype}");

            int index = Thermals.Allocate();
            PositionToIndex.Add(cell.Id, index);
            Thermals.list[index] = cell;

            CountPerFrame = GetCountPerFrame();
            ExternalBlockReset();
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                //MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            //MyLog.Default.Info($"[{Settings.Name}] Removed {Grid.EntityId} ({b.Position.Flatten()}) {b.Position}");

            int flat = b.Position.Flatten();
            int index = PositionToIndex[flat];
            ThermalCell cell = Thermals.list[index];

            if (RecentlyRemoved.ContainsKey(cell.Id))
            {
                RecentlyRemoved[cell.Id] = cell.Temperature;
            }
            else
            {
                RecentlyRemoved.Add(cell.Id, cell.Temperature);
            }

            cell.ClearNeighbors();
            PositionToIndex.Remove(flat);
            Thermals.Free(index);

            CountPerFrame = GetCountPerFrame();
            ExternalBlockReset();
        }

        private void GridSplit(MyCubeGrid g1, MyCubeGrid g2)
        {
            MyLog.Default.Info($"[{Settings.Name}] Grid Split - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.list[i];
                if (c == null) continue;

                if (tg1.RecentlyRemoved.ContainsKey(c.Id))
                {
                    c.Temperature = tg1.RecentlyRemoved[c.Id];
                    tg1.RecentlyRemoved.Remove(c.Id);
                }
            }

        }

        private void GridMerge(MyCubeGrid g1, MyCubeGrid g2)
        {
            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.list[i];
                if (c == null) continue;

                int id = c.Block.Position.Flatten();
                if (tg1.PositionToIndex.ContainsKey(id))
                {
                    tg1.Thermals.list[tg1.PositionToIndex[id]].Temperature = c.Temperature;
                }

            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Grid.Physics == null)
                NeedsUpdate = MyEntityUpdateEnum.NONE;

            PrepareNextTick();

            Load();
        }

        public override void UpdateBeforeSimulation()
        {
            IterationFrames++;

            int count = Thermals.UsedLength;
            int target = count - IterationIndex;
            if (CountPerFrame < target)
            {
                target = CountPerFrame;
            }

            target += IterationIndex;

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] Nodes: {count}, Frames/Cycle {Settings.Instance.Frequency} Nodes/Cycle: {CountPerFrame} Target: {target}, Index: {IterationIndex}", 1, "White");
            while (IterationIndex < target)
            {
                ThermalCell cell = Thermals.list[IterationIndex];
                if (cell != null)
                {
                    if (!ThermalCellUpdateComplete)
                    {
                        cell.UpdateSurfaces(ref ExposedNodes, ref ExposedSurface, ref InsideNodes, ref InsideSurface);
                    }

                    cell.Update();
                }

                IterationIndex++;
            }

            MapExternalBlocks();

            if (IterationIndex >= count && IterationFrames >= Settings.Instance.Frequency)
            {
                PrepareNextTick();

                IterationIndex = 0;
                IterationFrames = 0;

                if (!ThermalCellUpdateComplete)
                    ThermalCellUpdateComplete = true;

                if (!NodeUpdateComplete &&
                    ExposedQueue.Count == 0 &&
                    SolidQueue.Count == 0 &&
                    InsideQueue.Count == 0)
                {
                    NodeUpdateComplete = true;
                    ThermalCellUpdateComplete = false;
                }
            }
        }


        //public float FramePlanetDot;
        //public float FramePlanetDotRatio;
        //public float FramePlanetDiffernece;
        //public float FramePlanetAmbient;
        private void PrepareNextTick()
        {
            //reset this
            FrameSolarOccluded = false;

            FrameSolarDirection = MyVisualScriptLogicProvider.GetSunDirection();
            FrameMatrix = Grid.WorldMatrix;
            FrameGridCenter = Grid.PositionComp.WorldAABB.Center;

            SolarRadiationNode.Update();
            for (int i = 0; i < RadiationNodes.UsedLength; i++)
            {
                RadiationNodes.list[i]?.Update();
            }

            Vector3D position = Grid.PositionComp.WorldAABB.Center;
            PlanetManager.Planet p = PlanetManager.GetClosestPlanet(position);

            bool isUnderground = false;
            if (p != null)
            {
                PlanetDefinition def = p.Definition();
                Vector3 local = (position - p.Position);
                Vector3D surfacePointLocal = p.Entity.GetClosestSurfacePointLocal(ref local);
                isUnderground = local.LengthSquared() < surfacePointLocal.LengthSquared();
                float airDensity = p.Entity.GetAirDensity(position);

                float ambient = def.UndergroundTemperature;
                if (!isUnderground)
                {
                    float dot = (float)Vector3D.Dot(local.Normalized(), FrameSolarDirection);
                    //FramePlanetDot = dot;
                    //FramePlanetDotRatio = (dot + 1f) * 0.5f;
                    //FramePlanetDiffernece = (def.DayTemperature - def.NightTemperature);
                    ambient = def.NightTemperature + ((dot + 1f) * 0.5f * (def.DayTemperature - def.NightTemperature));
                    //FramePlanetAmbient = ambient;
                }
                else 
                {
                    FrameSolarOccluded = true;
                }

                FrameAmbientTemprature = ambient * airDensity;
                FrameSolarDecay = def.SolarDecay * airDensity;
                FrameAmbientStrength = Math.Max(Settings.Instance.VaccumeRadiationStrength, airDensity);

                //TODO: implement underground core temparatures
            }

            if (FrameSolarOccluded) return;

            LineD line = new LineD(position, position + (FrameSolarDirection * 100000000));
            //BoundingBoxD box = ray.GetBoundingBox();
            List<MyLineSegmentOverlapResult<MyEntity>> results = new List<MyLineSegmentOverlapResult<MyEntity>>();
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, results);
            LineD subLine;

            for (int i = 0; i < results.Count; i++)
            {
                MyLineSegmentOverlapResult<MyEntity> ent = results[i];
                MyEntity e = ent.Element;

                if (e is MyPlanet)
                {
                    MyPlanet myPlanet = e as MyPlanet;
                    Vector3D planetLocal = position - myPlanet.PositionComp.WorldMatrixRef.Translation;
                    Vector3D planetDirection = Vector3D.Normalize(planetLocal);
                    double dot = Vector3D.Dot(planetDirection, FrameSolarDirection);
                    double occlusionDot = PlanetManager.GetLargestOcclusionDotProduct(PlanetManager.GetVisualSize(planetLocal.Length(), myPlanet.AverageRadius));

                    if (dot < occlusionDot)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                } 
                
                if (e is MyVoxelBase)
                {
                    MyVoxelBase voxel = e as MyVoxelBase;
                    if (voxel.RootVoxel is MyPlanet) continue;

                    voxel.PositionComp.WorldAABB.Intersect(ref line, out subLine);
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    var green = Color.Green.ToVector4();
                    MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref green, 0.2f);

                    IHitInfo hit;
                    MyAPIGateway.Physics.CastRay(subLine.From, subLine.To, out hit, 28); // 28

                    if (hit != null)
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }
                
                if (e is MyCubeGrid && e.Physics != null && e.EntityId != Grid.EntityId)
                {
                    MyCubeGrid g = (e as MyCubeGrid);
                    List<MyCubeGrid> grids = new List<MyCubeGrid>();
                    g.GetConnectedGrids(GridLinkTypeEnum.Physical, grids);

                    for (int j = 0; j < grids.Count; j++) 
                    {
                        if (grids[j].EntityId == Grid.EntityId) continue;
                    }

                    g.PositionComp.WorldAABB.Intersect(ref line, out subLine);

                    var blue = Color.Blue.ToVector4();
                    MySimpleObjectDraw.DrawLine(subLine.From, subLine.To, MyStringId.GetOrCompute("Square"), ref blue, 0.2f);

                    Vector3I? hit = (e as MyCubeGrid).RayCastBlocks(subLine.From, subLine.To);

                    if (hit.HasValue) 
                    {
                        FrameSolarOccluded = true;
                        break;
                    }
                }
            }

            var color = (FrameSolarOccluded) ? Color.Red.ToVector4() : Color.White.ToVector4();
            MySimpleObjectDraw.DrawLine(position, position + (FrameSolarDirection * 100000000), MyStringId.GetOrCompute("Square"), ref color, 0.1f);
        }


        public int GetCountPerFrame()
        {
            return 1 + (Thermals.Count / Settings.Instance.Frequency);
        }

        /// <summary>
        /// gets a the thermal cell at a specific location
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public ThermalCell Get(Vector3I position)
        {
            int flat = position.Flatten();
            if (PositionToIndex.ContainsKey(flat))
            {
                return Thermals.list[PositionToIndex[flat]];
            }

            return null;
        }
    }
}
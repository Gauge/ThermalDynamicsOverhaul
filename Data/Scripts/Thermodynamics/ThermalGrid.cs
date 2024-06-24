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
        public Dictionary<int, int> PositionToIndex = new Dictionary<int, int>();
        public MyFreeList<ThermalCell> Thermals = new MyFreeList<ThermalCell>();
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();
        public ThermalRadiationNode SolarRadiationNode = new ThermalRadiationNode();
        public ThermalRadiationNode WindNode = new ThermalRadiationNode();

        /// <summary>
        /// current frame per second
        /// </summary>
        public byte FrameCount = 0;

        /// <summary>
        /// update loop index
        /// updates happen across frames
        /// </summary>
        private int SimulationIndex = 0;

        /// <summary>
        /// The number of cells to process in a 1 second interval
        /// </summary>
        private int SimulationQuota = 0;

        /// <summary>
        /// The fractional number of cells to process this frame
        /// the remainder is carried over to the next frame
        /// </summary>
        public float FrameQuota = 0;

        /// <summary>
        /// The total number of simulations since grid life
        /// </summary>
        public long SimulationFrame = 1;

        /// <summary>
        /// updates cycle between updating first to last, last to first
        /// this ensures an even distribution of heat.
        /// </summary>
        private int Direction = 1;


        public bool ThermalCellUpdateComplete = true;


        public Vector3 FrameWindDirection;
        public Vector3 FrameSolarDirection;
        public MatrixD FrameMatrix;

        public float FrameAmbientTemprature;
        public float FrameAmbientTempratureP4;
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

            Grid.OnGridSplit += GridSplit;
            Grid.OnGridMerge += GridMerge;

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            MyLog.Default.Info($"[{Settings.Name}] serializing");
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

                int t = (int)(c.Temperature * 1000);
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

                Thermals.list[PositionToIndex[id]].Temperature = f * 0.001f;

                //MyLog.Default.Info($"[{Settings.Name}] [Unpack] {id} {PositionToIndex[id]} {Thermals.list[PositionToIndex[id]].Block.BlockDefinition.Id} - T: {f * 0.001f}");
            }
        }

        private void Save()
        {
            //Stopwatch sw = Stopwatch.StartNew();

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
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        private void Load()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            if (Entity.Storage.ContainsKey(StorageGuid))
            {
                Unpack(Entity.Storage[StorageGuid]);
            }

            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }

        private void BlockAdded(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            ThermalCell cell = new ThermalCell(this, b);
            cell.AddAllNeighbors();

            int index = Thermals.Allocate();
            PositionToIndex.Add(cell.Id, index);
            Thermals.list[index] = cell;

            MapBlocks(b, true);
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            MyLog.Default.Info($"[{Settings.Name}] block removed");

            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            //MyLog.Default.Info($"[{Settings.Name}] [{Grid.EntityId}] Removed ({b.Position.Flatten()}) {b.Position}");

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

            ResetMapper();
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

            MyLog.Default.Info($"[{Settings.Name}] Grid Merge - G1: {g1.EntityId} G2: {g2.EntityId}");

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
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
            }

            Load();
            PrepareNextSimulationStep();
            SimulationQuota = GetSimulationQuota();
        }


        public override void UpdateBeforeSimulation()
        {
            
            FrameCount++;
            //MyAPIGateway.Utilities.ShowNotification($"[Loop] f: {MyAPIGateway.Session.GameplayFrameCounter} fc: {FrameCount} sf: {SimulationFrame} sq: {SimulationQuota}", 1, "White");

            // if you are done processing the required blocks this second
            // wait for the start of the next second interval
            if (SimulationQuota == 0)
            {
                if (FrameCount >= 60)
                {
                    SimulationQuota = GetSimulationQuota();
                    FrameCount = 0;
                    FrameQuota = 0;
                }
                else
                {
                    return;
                }
            }

            FrameQuota += GetFrameQuota();
            int cellCount = Thermals.UsedLength;

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] c: {count} frameC: {QuotaPerSecond} simC: {60f * QuotaPerSecond}", 1, "White");

            //Stopwatch sw = Stopwatch.StartNew();
            while (FrameQuota >= 1)
            {
                if (SimulationQuota == 0) break;

                // prepare for the next simulation after a full iteration
                if (SimulationIndex == cellCount || SimulationIndex == -1)
                {
                    // start a new simulation frame
                    SimulationFrame++;

                    MapExterior();
                    PrepareNextSimulationStep();

                    // reverse the index direction
                    Direction *= -1;
                    // make sure the end cells in the list go once per frame
                    SimulationIndex += Direction;

                    if (!ThermalCellUpdateComplete)
                        ThermalCellUpdateComplete = true;

                    if (!NodeUpdateComplete &&
                        ExteriorQueue.Count == 0)
                    {
                        NodeUpdateComplete = true;
                        ThermalCellUpdateComplete = false;
                    }
                }


                //MyLog.Default.Info($"[{Settings.Name}] Frame: {FrameCount} SimFrame: {SimulationFrame}: Index: {SimulationIndex} Quota: {SimulationQuota} FrameQuota:{FrameQuota}");
                
                ThermalCell cell = Thermals.list[SimulationIndex];
                if (cell != null)
                {
                    if (!ThermalCellUpdateComplete)
                    {
                        cell.UpdateSurfaces(ref ExteriorNodes, ref neighbors);
                    }

                    cell.Update();
                }

                FrameQuota--;
                SimulationQuota--;
                SimulationIndex += Direction;
            }
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [UpdateLoop] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");

        }

        private void PrepareNextSimulationStep()
        {
            SolarRadiationNode.Update();

            FrameSolarOccluded = false;
            FrameSolarDirection = MyVisualScriptLogicProvider.GetSunDirection();
            FrameMatrix = Grid.WorldMatrix;

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
                float windSpeed = p.Entity.GetWindSpeed(position);
                 

                float ambient = def.UndergroundTemperature;
                if (!isUnderground)
                {
                    float dot = (float)Vector3D.Dot(local.Normalized(), FrameSolarDirection);
                    ambient = def.NightTemperature + ((dot + 1f) * 0.5f * (def.DayTemperature - def.NightTemperature));
                }
                else
                {
                    FrameSolarOccluded = true;
                }

                FrameAmbientTemprature = Math.Max(2.7f, ambient * airDensity);
                FrameAmbientTempratureP4 = FrameAmbientTemprature * FrameAmbientTemprature * FrameAmbientTemprature * FrameAmbientTemprature;
                FrameSolarDecay = 1 - def.SolarDecay * airDensity;

                FrameWindDirection = Vector3.Cross(p.GravityComponent.GetWorldGravityNormalized(position), p.Entity.WorldMatrix.Forward).Normalized() * windSpeed;


                //TODO: implement underground core temparatures
            }
            if (FrameSolarOccluded) return;

            LineD line = new LineD(position, position + (FrameSolarDirection * 15000000));
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

            if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated) 
            {
                var color = (FrameSolarOccluded) ? Color.Red.ToVector4() : Color.White.ToVector4();
                var color2 = Color.LightGoldenrodYellow.ToVector4();
                MySimpleObjectDraw.DrawLine(position, position + (FrameSolarDirection * 15000000), MyStringId.GetOrCompute("Square"), ref color, 0.1f);
                MySimpleObjectDraw.DrawLine(position, position + FrameWindDirection, MyStringId.GetOrCompute("Square"), ref color2, 0.1f);
            }
        }


        /// <summary>
        /// Calculates thermal cell count second to match the desired simulation speed
        /// </summary>
        public int GetSimulationQuota()
        {
            return Math.Max(1, (int)(Thermals.UsedLength * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency));
        }

        /// <summary>
        /// Calculates the thermal cell count required each frame
        /// </summary>
        public float GetFrameQuota()
        {
            return 0.00000001f + ((Thermals.UsedLength * Settings.Instance.SimulationSpeed * Settings.Instance.Frequency) / 60f);
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
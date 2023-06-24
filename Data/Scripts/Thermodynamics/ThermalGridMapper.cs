using ProtoBuf.Meta;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    public partial class ThermalGrid : MyGameLogicComponent
    {
        private readonly Vector3I[] neighbors = new Vector3I[6]
        {
            new Vector3I(1, 0, 0),
            new Vector3I(-1, 0, 0),
            new Vector3I(0, 1, 0),
            new Vector3I(0, -1, 0),
            new Vector3I(0, 0, 1),
            new Vector3I(0, 0, -1)
        };

        public HashSet<Vector3I> ExposedNodes = new HashSet<Vector3I>();
        public HashSet<Vector3I> SolidNodes = new HashSet<Vector3I>();
        public HashSet<Vector3I> InsideNodes = new HashSet<Vector3I>();

        public HashSet<Vector3I> ExposedSurface = new HashSet<Vector3I>();
        public HashSet<Vector3I> InsideSurface = new HashSet<Vector3I>();

        public List<HashSet<Vector3I>> Rooms = new List<HashSet<Vector3I>>();

        public Queue<Vector3I> ExposedQueue = new Queue<Vector3I>();
        public Queue<Vector3I> SolidQueue = new Queue<Vector3I>();
        public Queue<Vector3I> InsideQueue = new Queue<Vector3I>();

        private Vector3I min;
        private Vector3I max;

        private bool RoomStartCheck = false;
        public int NodeCountPerFrame = 1;
        public bool NodeUpdateComplete = false;

        public void ExternalBlockReset()
        {
            min = Grid.Min - 1;
            max = Grid.Max + 1;

            ExposedQueue.Clear();
            ExposedQueue.Enqueue(min);

            ExposedNodes.Clear();
            ExposedNodes.Add(min);

            SolidNodes.Clear();
            SolidQueue.Clear();

            InsideNodes.Clear();
            InsideQueue.Clear();

            ExposedSurface.Clear();
            InsideSurface.Clear();

            Rooms.Clear();
            RoomStartCheck = false;

            NodeCountPerFrame = Math.Max((int)((max - min).Size / 60f), 1);
            NodeUpdateComplete = false;
        }

        public void MapExternalBlocks()
        {
            if (SolidQueue.Count == 0 && InsideQueue.Count == 0 && ExposedQueue.Count == 0) return;

            int loopCount = 0;

            CrawlOutside(ref loopCount);

            // Yes, CrawlBlocks Should be sandwitched between two CrawlInside calls
            CrawlInside(ref loopCount);

            CrawlBlocks(ref loopCount);

            CrawlInside(ref loopCount);

            //string text = "";
            //for (int i = 0; i < Rooms.Count; i++)
           // {
            //    text += $" R{i} {Rooms[i].Count}";
            //}

            //MyLog.Default.Info($"[Mapper] {Grid.EntityId} ({loopCount},{ExposedQueue.Count},{InsideQueue.Count},{SolidQueue.Count})  Exposed {ExposedNodes.Count} ExposedSurface {ExposedSurface.Count} Solid {SolidNodes.Count} Inside {InsideNodes.Count} Rooms: {Rooms.Count}" + text);
        }

        private void CrawlInside(ref int loopCount) 
        {
            while (InsideQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;
                Vector3I block = InsideQueue.Dequeue();
                HashSet<Vector3I> room = Rooms[Rooms.Count - 1];

                for (int i = 0; i < neighbors.Length; i++)
                {
                    Vector3I n = block + neighbors[i];

                    if (InsideNodes.Contains(n) || SolidNodes.Contains(n) || ExposedNodes.Contains(n) || AreNodesAirtight(block, n))
                        continue;

                    room.Add(n);
                    InsideNodes.Add(n);
                    InsideQueue.Enqueue(n);
                }
            }
        }

        private void CrawlBlocks(ref int loopCount) 
        {
            while (SolidQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;
                Vector3I block = SolidQueue.Dequeue();

                for (int i = 0; i < neighbors.Length; i++)
                {
                    Vector3I n = block + neighbors[i];

                    if (Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max || SolidNodes.Contains(n) || ExposedNodes.Contains(n) || InsideNodes.Contains(n) )
                        continue;

                    if (!AreNodesAirtight(block, n))
                    {
                        // if inside is found start working on it right away
                        // this block can be redone later
                        SolidQueue.Enqueue(block);
                        InsideQueue.Enqueue(n);
                        
                        Rooms.Add(new HashSet<Vector3I>());
                        
                        InsideNodes.Add(n);
                        Rooms[Rooms.Count - 1].Add(n);

                        return;
                    }

                    SolidNodes.Add(n);
                    SolidQueue.Enqueue(n);
                }
            }
        }

        private void CrawlOutside(ref int loopCount) 
        {
            while (ExposedQueue.Count > 0 && loopCount < NodeCountPerFrame)
            {
                loopCount++;
                Vector3I block = ExposedQueue.Dequeue();

                for (int i = 0; i < neighbors.Length; i++)
                {
                    Vector3I n = block + neighbors[i];

                    if (Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max || ExposedNodes.Contains(n))
                        continue;

                    if (AreNodesAirtight(block, n, ref ExposedSurface))
                    {
                        if (SolidQueue.Count == 0)
                            SolidQueue.Enqueue(n);
                        continue;
                    }

                    ExposedNodes.Add(n);
                    ExposedQueue.Enqueue(n);
                }
            }
        }

        private bool AreNodesAirtight(Vector3I start, Vector3I end)
        {
            // the the point being moved to is empty it is not air tight
            IMySlimBlock target = Grid.GetCubeBlock(end);
            if (target == null)
                return false;

            IMySlimBlock current = Grid.GetCubeBlock(start);

            // verify that the block is fully built and airtight
            if (current == target)
            {
                return (current.BlockDefinition as MyCubeBlockDefinition)?.IsAirTight == true;
            }

            return (current != null && IsAirtightBlock(current, start, end - start)) ||
                IsAirtightBlock(target, end, start - end);
        }

        private bool AreNodesAirtight(Vector3I start, Vector3I end, ref HashSet<Vector3I> surfaces)
        {
            // the the point being moved to is empty it is not air tight
            IMySlimBlock target = Grid.GetCubeBlock(end);
            if (target == null) {
                surfaces.Add(end);
                return false;
            }
                
            IMySlimBlock current = Grid.GetCubeBlock(start);

            // verify that the block is fully built and airtight
            if (current == target)
            {
                return (current.BlockDefinition as MyCubeBlockDefinition)?.IsAirTight == true;
            }

            return (current != null && IsAirtightBlock(current, start, end - start)) ||
                IsAirtightBlock(target, end, start - end);
        }

        private bool IsAirtightBlock(IMySlimBlock block, Vector3I pos, Vector3 normal)
        {
            MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
            if (def == null)
            {
                return false;
            }

            if (def.IsAirTight.HasValue)
            {
                return def.IsAirTight.Value;
            }

            Matrix result;
            block.Orientation.GetMatrix(out result);
            result.TransposeRotationInPlace();
            Vector3I transformedNormal = Vector3I.Round(Vector3.Transform(normal, result));
            Vector3 position = Vector3.Zero;

            if (block.FatBlock != null)
            {
                position = pos - block.FatBlock.Position;
            }

            IMyDoor door = block.FatBlock as IMyDoor;
            bool isDoorClosed = door != null && (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing);

            Vector3 value = Vector3.Transform(position, result) + def.Center;
            switch (def.IsCubePressurized[Vector3I.Round(value)][transformedNormal])
            {
                case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
                    return true;
                case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed:
                    {
                        if (isDoorClosed)
                        {
                            return true;
                        }
                        break;
                    }
            }

            return isDoorClosed && IsDoorAirtight(door, ref transformedNormal, def);
        }

        private bool IsDoorAirtight(IMyDoor door, ref Vector3I normal, MyCubeBlockDefinition def)
        {
            if (!door.IsFullyClosed)
                return false;

            if (door is MyAirtightSlideDoor)
            {
                if (normal == Vector3I.Forward)
                {
                    return true;
                }
            }
            else if (door is MyAirtightDoorGeneric)
            {
                if (normal == Vector3I.Forward || normal == Vector3I.Backward)
                {
                    return true;
                }
            }

            // standard and advanced doors
            MyCubeBlockDefinition.MountPoint[] mountPoints = def.MountPoints;
            for (int i = 0; i < mountPoints.Length; i++)
            {
                MyCubeBlockDefinition.MountPoint mountPoint2 = mountPoints[i];
                if (normal == mountPoint2.Normal)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
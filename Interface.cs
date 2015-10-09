﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Math3D;
using Protocol;
using Logging;

namespace TechniteLogic
{
	public static class Interface
	{
		public enum ChannelID
		{
			Unused,
			Ready,					//c2s: <signal>
			TechniteStateChunk,     //s2c: TechniteStateChunk
			InstructTechnites,      //s2c: <signal>
			TechniteInstructionChunk, //c2s: TechniteInstructionChunk

			NodeChunk,      //s2c: NodeChunk - completed before SessionBegin is sent
			GridConfig,		//s2c: GridConfig
			GridDelta,      //s2c: GridDelta
			WorldInfo,		//s2c: WorldInfo

			RequestNextRound, //c2s: <signal>

			Count     //must remain last
		};


		public static OutChannel<Struct.TechniteInstructionChunk> techniteInstructionChunk = new Protocol.OutChannel<Struct.TechniteInstructionChunk>((uint)ChannelID.TechniteInstructionChunk);
		public static SignalChannel ready = new SignalChannel((uint)ChannelID.Ready);
		public static SignalChannel requestNextRound = new SignalChannel((uint)ChannelID.RequestNextRound);

		public static void Register()
        {
			ChannelMap.Register<Struct.TechniteStateChunk>((uint)ChannelID.TechniteStateChunk, Event.TechniteStateChunk);
			ChannelMap.RegisterSignal((uint)ChannelID.InstructTechnites,Event.InstructTechnites);
			ChannelMap.Register<Struct.GridConfig>((uint)ChannelID.GridConfig, Event.GridConfig);
			ChannelMap.Register<Struct.NodeChunk>((uint)ChannelID.NodeChunk, Event.NodeChunk);
			ChannelMap.Register<Struct.GridDelta>((uint)ChannelID.GridDelta, Event.GridDelta);
			ChannelMap.Register<Struct.WorldInfo>((uint)ChannelID.WorldInfo, Event.WorldInfo);
        }


		public class Struct
		{

			public struct TechniteResources
			{
				public byte energy,
							matter;


			}

			public struct WorldInfo
			{
				public byte coreContent;
			}

			public struct TechniteState
			{
				public UInt32 location;
				public TechniteResources resources;
				public byte taskResult,
							state;

			}


			public enum TechniteChunkFlags
			{
				IsFirst = 0x1,
				IsLast = 0x2

			}

			public struct TechniteStateChunk
			{
				public byte flags;
				public TechniteState[] states;


				public bool FlagIsSet(TechniteChunkFlags flag)
				{
					return ((int)flags & (int)flag) != 0;
				}

			}

			public struct TechniteInstruction
			{
				public byte nextTask,
							taskTarget,
							taskParameter;
			}

			public struct TechniteInstructionChunk
			{
				public const int MaxPerChunk = 10000;
				public UInt32 offset;
				public TechniteInstruction[] instructions;
			}


			public struct GridDeltaBlock
			{
				public UInt32 repitition;
				public byte value;
			}

			public struct GridDelta
			{
				public UInt32 nodeOffset,
								nodeCount;
				public GridDeltaBlock[] contentBlocks,
										structureCountBlocks,
										techniteFactionBlocks;

			}

			public struct GridNode
            {
                public UInt32[] neighbors;
                public Vec3 stackBase;
                public Vec3 stackDirection;
            }

			public struct GridConfig
			{
				public float heightPerLayer;
				public int numLayersPerStack;
				public byte[] matterYieldByContentType;
			}

			public struct NodeChunk
			{
				public bool isLast;
				public GridNode[] nodes;
			}

			//public struct SessionBegin
			//{
			//	public GridConfig grid;
			//	public byte myFactionGridID;
			//}
        }

        public static class Event
		{

			public static void InstructTechnites(Protocol.Client cl)
			{
				Technite.Cleanup();	//updates must be done by now
				Logic.ProcessTechnites();

				int numTechnites = Technite.Count;
				int numChunks = numTechnites / Struct.TechniteInstructionChunk.MaxPerChunk;
				if ((numTechnites % Struct.TechniteInstructionChunk.MaxPerChunk)!= 0)
					numChunks++;

				Out.Log(Significance.Common, "Sending "+numChunks+" technite data response chunks");
				var e = Technite.All.GetEnumerator();
				int offset = 0;
				for (int i = 0; i < numChunks; i++)
				{
					int chunkSize = Math.Min(Struct.TechniteInstructionChunk.MaxPerChunk, numTechnites - offset);

					Struct.TechniteInstructionChunk chunk = new Struct.TechniteInstructionChunk();
					chunk.offset = (uint)offset;
					chunk.instructions = new Struct.TechniteInstruction[chunkSize];
					for (int j = 0; j < chunkSize; j++)
					{
						bool success = e.MoveNext();
						Debug.Assert(success);
                        Technite t = e.Current;
						chunk.instructions[j] = t.ExportInstruction();
					}

					techniteInstructionChunk.SendTo(cl,chunk);

					offset += chunkSize;
				}
			}

			public static void TechniteStateChunk(Protocol.Client cl, Struct.TechniteStateChunk chunk)
			{
				Out.Log(Significance.Common, "Received technite state chunk containing "+chunk.states.Length+" technites");
				if (chunk.FlagIsSet(Struct.TechniteChunkFlags.IsFirst))
					Technite.Reset();
				foreach (var state in chunk.states)
					Technite.CreateOrUpdate(state);
				if (chunk.FlagIsSet(Struct.TechniteChunkFlags.IsLast))
					Technite.Cleanup();

			}

			internal static void GridDelta(Protocol.Client cl, Struct.GridDelta gridDelta)
			{
				Grid.ApplyDelta(gridDelta);
			}

			internal static void NodeChunk(Protocol.Client cl, Struct.NodeChunk nodeChunk)
			{
				Grid.Graph.AddChunk(nodeChunk);
			}

			internal static void GridConfig(Protocol.Client cl, Struct.GridConfig gridConfig)
			{
				Grid.BeginSession(gridConfig.heightPerLayer, gridConfig.numLayersPerStack);
				if (Technite.MatterYield.Length != gridConfig.matterYieldByContentType.Length)
				{
					Out.Log(Significance.ProgramFatal, "Received matter yield vector (" + gridConfig.matterYieldByContentType.Length + ") does not match number of supported content types (" + Technite.MatterYield.Length + ").");
					return;
				}

				Technite.MatterYield = gridConfig.matterYieldByContentType;
            }

			internal static void WorldInfo(Protocol.Client cl, Struct.WorldInfo worldInfo)
			{
				Grid.World.Setup((Grid.Content)worldInfo.coreContent);

			}
		}
	}
}

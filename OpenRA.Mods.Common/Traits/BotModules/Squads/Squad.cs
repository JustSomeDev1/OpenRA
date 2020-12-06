#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	public enum SquadType { Assault, Air, Rush, Protection, Naval }

	public class Squad
	{
		public List<Actor> Units = new List<Actor>();
		public SquadType Type;

		internal IBot Bot;
		internal World World;
		internal SquadManagerBotModule SquadManager;
		internal MersenneTwister Random;

		internal Target Target;
		internal StateMachine FuzzyStateMachine;

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type)
			: this(bot, squadManager, type, Target.Invalid) { }

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type, Target target)
		{
			Bot = bot;
			SquadManager = squadManager;
			World = bot.Player.PlayerActor.World;
			Random = World.LocalRandom;
			Type = type;
			Target = target;
			FuzzyStateMachine = new StateMachine();

			switch (type)
			{
				case SquadType.Assault:
				case SquadType.Rush:
					FuzzyStateMachine.ChangeState(this, new GroundUnitsIdleState(), true);
					break;
				case SquadType.Air:
					FuzzyStateMachine.ChangeState(this, new AirIdleState(), true);
					break;
				case SquadType.Protection:
					FuzzyStateMachine.ChangeState(this, new UnitsForProtectionIdleState(), true);
					break;
				case SquadType.Naval:
					FuzzyStateMachine.ChangeState(this, new NavyUnitsIdleState(), true);
					break;
			}
		}

		public void Update()
		{
			if (IsValid)
				FuzzyStateMachine.Update(this);
		}

		public bool IsValid { get { return Units.Any(); } }

		public bool IsTargetValid
		{
			get { return Target.IsValidFor(Units.FirstOrDefault()) && !Target.Actor.Info.HasTraitInfo<HuskInfo>(); }
		}

		public bool IsTargetVisible
		{
			get
			{
				if (Target.Type != TargetType.Actor)
					return false;

				return Target.Actor.CanBeViewedByPlayer(Bot.Player);
			}
		}

		public WPos CenterPosition { get { return Units.Select(u => u.CenterPosition).Average(); } }

		public MiniYaml Serialize()
		{
			var nodes = new MiniYaml("", new List<MiniYamlNode>()
			{
				new MiniYamlNode("Type", FieldSaver.FormatValue(Type)),
				new MiniYamlNode("Units", FieldSaver.FormatValue(Units.Select(a => a.ActorID).ToArray())),
			});

			if (Target.Type == TargetType.Actor)
				nodes.Nodes.Add(new MiniYamlNode("TargetActor", FieldSaver.FormatValue(Target.Actor.ActorID)));
			else if (Target.Type == TargetType.FrozenActor)
				nodes.Nodes.Add(new MiniYamlNode("TargetFrozenActor", FieldSaver.FormatValue(Target.FrozenActor.ID)));
			else if (Target.Type == TargetType.Terrain)
				nodes.Nodes.Add(new MiniYamlNode("TargetTerrain", FieldSaver.FormatValue(Target.CenterPosition)));

			return nodes;
		}

		public static Squad Deserialize(IBot bot, SquadManagerBotModule squadManager, MiniYaml yaml)
		{
			var type = SquadType.Rush;
			var target = Target.Invalid;

			var typeNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Type");
			if (typeNode != null)
				type = FieldLoader.GetValue<SquadType>("Type", typeNode.Value.Value);

			var targetActorNode = yaml.Nodes.FirstOrDefault(n => n.Key == "TargetActor");
			if (targetActorNode != null)
			{
				var targetActorId = FieldLoader.GetValue<uint>("TargetActor", targetActorNode.Value.Value);
				target = Target.FromActor(squadManager.World.GetActorById(targetActorId));
			}

			var targetFrozenActorNode = yaml.Nodes.FirstOrDefault(n => n.Key == "TargetFrozenActor");
			if (targetFrozenActorNode != null)
			{
				var targetFrozenActorId = FieldLoader.GetValue<uint>("TargetFrozenActor", targetFrozenActorNode.Value.Value);
				target = Target.FromFrozenActor(bot.Player.FrozenActorLayer.FromID(targetFrozenActorId));
			}

			var targetTerrainNode = yaml.Nodes.FirstOrDefault(n => n.Key == "TargetTerrain");
			if (targetTerrainNode != null)
			{
				var targetPos = FieldLoader.GetValue<WPos>("TargetTerrain", targetFrozenActorNode.Value.Value);
				target = Target.FromPos(targetPos);
			}

			var squad = new Squad(bot, squadManager, type, target);
			var unitsNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Units");
			if (unitsNode != null)
				squad.Units.AddRange(FieldLoader.GetValue<uint[]>("Units", unitsNode.Value.Value)
					.Select(a => squadManager.World.GetActorById(a)));

			return squad;
		}
	}
}

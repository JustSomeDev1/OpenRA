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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	abstract class GroundStateBase : StateBase
	{
		protected virtual bool ShouldFlee(Squad squad)
		{
			return ShouldFlee(squad, enemies => !AttackOrFleeFuzzy.Default.CanAttack(squad.Units, enemies));
		}

		protected Actor FindClosestEnemy(Squad squad)
		{
			return squad.SquadManager.FindClosestEnemy(squad.Units.First().CenterPosition);
		}
	}

	class GroundUnitsIdleState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (!squad.IsTargetValid)
			{
				var closestEnemy = FindClosestEnemy(squad);
				if (closestEnemy == null)
					return;

				squad.TargetActor = closestEnemy;
			}

			var enemyUnits = squad.World.FindActorsInCircle(squad.TargetActor.CenterPosition, WDist.FromCells(squad.SquadManager.Info.IdleScanRadius))
				.Where(squad.SquadManager.IsPreferredEnemyUnit).ToList();

			if (enemyUnits.Count == 0)
				return;

			if (AttackOrFleeFuzzy.Default.CanAttack(squad.Units, enemyUnits))
			{
				foreach (var u in squad.Units)
					squad.Bot.QueueOrder(new Order("AttackMove", u, Target.FromCell(squad.World, squad.TargetActor.Location), false));

				// We have gathered sufficient units. Attack the nearest enemy unit.
				squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsAttackMoveState(), true);
			}
			else
				squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class GroundUnitsAttackMoveState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (!squad.IsTargetValid)
			{
				var closestEnemy = FindClosestEnemy(squad);
				if (closestEnemy != null)
					squad.TargetActor = closestEnemy;
				else
				{
					squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsFleeState(), true);
					return;
				}
			}

			var leader = squad.Units.ClosestTo(squad.TargetActor.CenterPosition);
			if (leader == null)
				return;

			var ownUnits = squad.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(squad.Units.Count) / 3)
				.Where(a => a.Owner == squad.Units.First().Owner && squad.Units.Contains(a)).ToHashSet();

			if (ownUnits.Count < squad.Units.Count)
			{
				// Since units have different movement speeds, they get separated while approaching the target.
				// Let them regroup into tighter formation.
				squad.Bot.QueueOrder(new Order("Stop", leader, false));
				foreach (var unit in squad.Units.Where(a => !ownUnits.Contains(a)))
					squad.Bot.QueueOrder(new Order("AttackMove", unit, Target.FromCell(squad.World, leader.Location), false));
			}
			else
			{
				var enemies = squad.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(squad.SquadManager.Info.AttackScanRadius))
					.Where(squad.SquadManager.IsPreferredEnemyUnit);
				var target = enemies.ClosestTo(leader.CenterPosition);
				if (target != null)
				{
					squad.TargetActor = target;
					squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsAttackState(), true);
				}
				else
					foreach (var a in squad.Units)
						squad.Bot.QueueOrder(new Order("AttackMove", a, Target.FromCell(squad.World, squad.TargetActor.Location), false));
			}

			if (ShouldFlee(squad))
				squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class GroundUnitsAttackState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (!squad.IsTargetValid)
			{
				var closestEnemy = FindClosestEnemy(squad);
				if (closestEnemy != null)
					squad.TargetActor = closestEnemy;
				else
				{
					squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsFleeState(), true);
					return;
				}
			}

			foreach (var a in squad.Units)
				if (!BusyAttack(a))
					squad.Bot.QueueOrder(new Order("Attack", a, Target.FromActor(squad.TargetActor), false));

			if (ShouldFlee(squad))
				squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class GroundUnitsFleeState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			GoToRandomOwnBuilding(squad);
			squad.FuzzyStateMachine.ChangeState(squad, new GroundUnitsIdleState(), true);
		}

		public void Deactivate(Squad squad) { squad.Units.Clear(); }
	}
}

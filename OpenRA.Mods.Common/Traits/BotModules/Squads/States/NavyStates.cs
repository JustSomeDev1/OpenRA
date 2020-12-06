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
	abstract class NavyStateBase : StateBase
	{
		protected virtual bool ShouldFlee(Squad squad)
		{
			return ShouldFlee(squad, enemies => !AttackOrFleeFuzzy.Default.CanAttack(squad.Units, enemies));
		}

		protected Actor FindClosestEnemy(Squad squad)
		{
			var first = squad.Units.First();

			// Navy squad AI can exploit enemy naval production to find path, if any.
			// (Way better than finding a nearest target which is likely to be on Ground)
			// You might be tempted to move these lookups into Activate() but that causes null reference exception.
			var domainIndex = first.World.WorldActor.Trait<DomainIndex>();
			var locomotor = first.Trait<Mobile>().Locomotor;

			var navalProductions = squad.World.ActorsHavingTrait<Building>().Where(a
				=> squad.SquadManager.Info.NavalProductionTypes.Contains(a.Info.Name)
				   && domainIndex.IsPassable(first.Location, a.Location, locomotor)
				   && a.AppearsHostileTo(first));

			if (navalProductions.Any())
			{
				var nearest = navalProductions.ClosestTo(first);

				// Return nearest when it is FAR enough.
				// If the naval production is within MaxBaseRadius, it implies that
				// this squad is close to enemy territory and they should expect a naval combat;
				// closest enemy makes more sense in that case.
				if ((nearest.Location - first.Location).LengthSquared > squad.SquadManager.Info.MaxBaseRadius * squad.SquadManager.Info.MaxBaseRadius)
					return nearest;
			}

			return squad.SquadManager.FindClosestEnemy(first.CenterPosition);
		}
	}

	class NavyUnitsIdleState : NavyStateBase, IState
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

				squad.Target = Target.FromActor(closestEnemy);
			}

			var enemyUnits = squad.World.FindActorsInCircle(squad.Target.CenterPosition, WDist.FromCells(squad.SquadManager.Info.IdleScanRadius))
				.Where(squad.SquadManager.IsPreferredEnemyUnit).ToList();

			if (enemyUnits.Count == 0)
				return;

			if (AttackOrFleeFuzzy.Default.CanAttack(squad.Units, enemyUnits))
			{
				foreach (var u in squad.Units)
					squad.Bot.QueueOrder(new Order("AttackMove", u, Target.FromPos(squad.Target.CenterPosition), false));

				// We have gathered sufficient units. Attack the nearest enemy unit.
				squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsAttackMoveState(), true);
			}
			else
				squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class NavyUnitsAttackMoveState : NavyStateBase, IState
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
					squad.Target = Target.FromActor(closestEnemy);
				else
				{
					squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsFleeState(), true);
					return;
				}
			}

			var leader = squad.Units.ClosestTo(squad.Target.CenterPosition);
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
					squad.Bot.QueueOrder(new Order("AttackMove", unit, Target.FromPos(leader.CenterPosition), false));
			}
			else
			{
				var enemies = squad.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(squad.SquadManager.Info.AttackScanRadius))
					.Where(squad.SquadManager.IsPreferredEnemyUnit);
				var target = enemies.ClosestTo(leader.CenterPosition);
				if (target != null)
				{
					squad.Target = Target.FromActor(target);
					squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsAttackState(), true);
				}
				else
					foreach (var a in squad.Units)
						squad.Bot.QueueOrder(new Order("AttackMove", a, Target.FromPos(squad.Target.CenterPosition), false));
			}

			if (ShouldFlee(squad))
				squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class NavyUnitsAttackState : NavyStateBase, IState
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
					squad.Target = Target.FromActor(closestEnemy);
				else
				{
					squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsFleeState(), true);
					return;
				}
			}

			foreach (var a in squad.Units)
				if (!BusyAttack(a))
					squad.Bot.QueueOrder(new Order("Attack", a, squad.Target, false));

			if (ShouldFlee(squad))
				squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsFleeState(), true);
		}

		public void Deactivate(Squad squad) { }
	}

	class NavyUnitsFleeState : NavyStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			GoToRandomOwnBuilding(squad);
			squad.FuzzyStateMachine.ChangeState(squad, new NavyUnitsIdleState(), true);
		}

		public void Deactivate(Squad squad) { squad.Units.Clear(); }
	}
}

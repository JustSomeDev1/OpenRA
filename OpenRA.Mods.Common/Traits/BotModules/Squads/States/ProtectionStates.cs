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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	class UnitsForProtectionIdleState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }
		public void Tick(Squad squad) { squad.FuzzyStateMachine.ChangeState(squad, new UnitsForProtectionAttackState(), true); }
		public void Deactivate(Squad squad) { }
	}

	class UnitsForProtectionAttackState : GroundStateBase, IState
	{
		public const int BackoffTicks = 4;
		internal int Backoff = BackoffTicks;

		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			if (!squad.IsTargetValid)
			{
				squad.TargetActor = squad.SquadManager.FindClosestEnemy(squad.CenterPosition, WDist.FromCells(squad.SquadManager.Info.ProtectionScanRadius));

				if (squad.TargetActor == null)
				{
					squad.FuzzyStateMachine.ChangeState(squad, new UnitsForProtectionFleeState(), true);
					return;
				}
			}

			if (!squad.IsTargetVisible)
			{
				if (Backoff < 0)
				{
					squad.FuzzyStateMachine.ChangeState(squad, new UnitsForProtectionFleeState(), true);
					Backoff = BackoffTicks;
					return;
				}

				Backoff--;
			}
			else
			{
				foreach (var a in squad.Units)
					squad.Bot.QueueOrder(new Order("AttackMove", a, Target.FromCell(squad.World, squad.TargetActor.Location), false));
			}
		}

		public void Deactivate(Squad squad) { }
	}

	class UnitsForProtectionFleeState : GroundStateBase, IState
	{
		public void Activate(Squad squad) { }

		public void Tick(Squad squad)
		{
			if (!squad.IsValid)
				return;

			GoToRandomOwnBuilding(squad);
			squad.FuzzyStateMachine.ChangeState(squad, new UnitsForProtectionIdleState(), true);
		}

		public void Deactivate(Squad squad) { squad.Units.Clear(); }
	}
}

using Godot;
using System.Collections.Generic;

namespace PlayGround.Player;

public sealed class PlayerAttack
{
	private static readonly StringName FireAction = "primary_attack";

	private readonly Player _owner;
	private readonly int _maxAttackCount;
	private readonly List<ProjectileAttack> _attacks = new();
	private readonly List<AoeAttack> _aoeAttacks = new();

	public PlayerAttack(
		Player owner,
		int maxAttackCount)
	{
		_owner = owner;
		_maxAttackCount = Mathf.Max(0, maxAttackCount);
	}

	public int Count => _attacks.Count + _aoeAttacks.Count;

	public void Setup()
	{
		EquipConfiguredAttacks();
	}

	public void Update(double delta, Vector2 aimWorldPosition)
	{
		for (int i = 0; i < _attacks.Count; i++)
		{
			_attacks[i].update(delta, aimWorldPosition);
		}

		for (int i = 0; i < _aoeAttacks.Count; i++)
		{
			_aoeAttacks[i].update(delta, aimWorldPosition);
		}

		if (!InputMap.HasAction(FireAction) || !Input.IsActionPressed(FireAction))
		{
			return;
		}

		TryPerformAll(aimWorldPosition);
	}

	public int TryPerformAll(Vector2 aimWorldPosition)
	{
		int performedCount = 0;
		for (int i = 0; i < _attacks.Count; i++)
		{
			if (_attacks[i].try_perform(aimWorldPosition))
			{
				performedCount++;
			}
		}

		for (int i = 0; i < _aoeAttacks.Count; i++)
		{
			if (_aoeAttacks[i].try_perform(aimWorldPosition))
			{
				performedCount++;
			}
		}

		return performedCount;
	}

	private void EquipConfiguredAttacks()
	{
		foreach (Node child in _owner.GetChildren())
		{
			if (Count >= _maxAttackCount)
			{
				break;
			}

			if (child is ProjectileAttack attack)
			{
				attack.configure(_owner);
				_attacks.Add(attack);
			}
			else if (child is AoeAttack aoeAttack)
			{
				aoeAttack.configure(_owner);
				_aoeAttacks.Add(aoeAttack);
			}
		}
	}
}

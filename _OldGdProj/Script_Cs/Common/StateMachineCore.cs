using Godot;
using System;
using System.Collections.Generic;

namespace PlayGround.Common;

public partial class StateMachineCore : RefCounted
{
    public event Action<int, int>? StateChanged;

    public int CurrentState { get; private set; } = -1;
    public int current_state => CurrentState;

    private readonly Dictionary<Vector2I, Action> _actions = new();
    private readonly Dictionary<Vector2I, Callable> _callables = new();

    public void Init(int initialState)
    {
        CurrentState = initialState;
        StateChanged?.Invoke(-1, initialState);
    }

    public void init(int initialState)
    {
        Init(initialState);
    }

    public void OnTransition(int from, int to, Action callback)
    {
        _actions[new Vector2I(from, to)] = callback;
    }

    public void on_transition(int from, int to, Callable callback)
    {
        _callables[new Vector2I(from, to)] = callback;
    }

    public bool TransitionTo(int newState)
    {
        if (CurrentState == newState)
        {
            return false;
        }

        var key = new Vector2I(CurrentState, newState);
        if (_actions.TryGetValue(key, out Action? action))
        {
            action();
        }
        if (_callables.TryGetValue(key, out Callable callable))
        {
            callable.Call();
        }

        int oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(oldState, newState);
        return true;
    }

    public bool transition_to(int newState)
    {
        return TransitionTo(newState);
    }
}

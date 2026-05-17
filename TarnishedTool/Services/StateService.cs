using System;
using System.Collections.Generic;
using TarnishedTool.Enums;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;

namespace TarnishedTool.Services;

public class StateService(IMemoryService memoryService) : IStateService
{
    private readonly Dictionary<State, List<Action>> _eventHandlers = new();

    public bool IsLoaded()
    {
        var worldChrman = memoryService.Read<nint>(Offsets.WorldChrMan.Base);
        return memoryService.Read<nint>(worldChrman + Offsets.WorldChrMan.PlayerIns) != 0;
    }

    public void Publish(State eventType)
    {
        if (_eventHandlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers)
                handler.Invoke();
        }
    }

    public void Subscribe(State eventType, Action handler)
    {
        if (!_eventHandlers.TryGetValue(eventType, out var handlers))
        {
            handlers = new List<Action>();
            _eventHandlers[eventType] = handlers;
        }

        handlers.Add(handler);
    }

    public void Unsubscribe(State eventType, Action handler)
    {
        if (_eventHandlers.ContainsKey(eventType))
            _eventHandlers[eventType].Remove(handler);
    }
}
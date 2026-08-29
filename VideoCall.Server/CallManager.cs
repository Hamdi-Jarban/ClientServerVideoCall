using System.Collections.Concurrent;
using VideoCall.Shared.Messages;
using VideoCall.Shared.Models;

namespace VideoCall.Server;

public enum CallOperationResult
{
    Ok,
    TargetOffline,
    TargetBusy,
    CallNotFound,
    NotYourCall,
    InvalidState
}


public class CallManager
{
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, CallSession> _calls = new();
  
    private readonly ConcurrentDictionary<string, Guid> _activeCallByUser = new(StringComparer.OrdinalIgnoreCase);

    public (CallOperationResult Result, CallSession? Call) StartCall(string caller, string callee, UserManager users)
    {
        if (!users.IsOnline(callee))
        {
            return (CallOperationResult.TargetOffline, null);
        }

        if (_activeCallByUser.ContainsKey(callee) || _activeCallByUser.ContainsKey(caller))
        {
            return (CallOperationResult.TargetBusy, null);
        }

        var call = new CallSession { Caller = caller, Callee = callee, State = CallState.Calling };
        _calls[call.CallId] = call;
        _activeCallByUser[caller] = call.CallId;
        _activeCallByUser[callee] = call.CallId;

        call.State = CallState.Ringing;


        _ = ExpireIfStillRingingAsync(call.CallId);

        return (CallOperationResult.Ok, call);
    }

    public CallOperationResult Accept(Guid callId, string acceptingUser, out CallSession? call)
    {
        if (!_calls.TryGetValue(callId, out call))
        {
            return CallOperationResult.CallNotFound;
        }

        if (!string.Equals(call.Callee, acceptingUser, StringComparison.OrdinalIgnoreCase))
        {
            return CallOperationResult.NotYourCall;
        }

        if (call.State != CallState.Ringing)
        {
            return CallOperationResult.InvalidState;
        }

        call.State = CallState.Connected;
        return CallOperationResult.Ok;
    }

    public CallOperationResult Reject(Guid callId, string rejectingUser, out CallSession? call)
    {
        if (!_calls.TryGetValue(callId, out call))
        {
            return CallOperationResult.CallNotFound;
        }

        if (!string.Equals(call.Callee, rejectingUser, StringComparison.OrdinalIgnoreCase))
        {
            return CallOperationResult.NotYourCall;
        }

        if (call.State != CallState.Ringing)
        {
            return CallOperationResult.InvalidState;
        }

        call.State = CallState.Rejected;
        ReleaseParticipants(call);
        return CallOperationResult.Ok;
    }

    public CallOperationResult End(Guid callId, string endingUser, out CallSession? call)
    {
        if (!_calls.TryGetValue(callId, out call))
        {
            return CallOperationResult.CallNotFound;
        }

        bool isParticipant = string.Equals(call.Caller, endingUser, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(call.Callee, endingUser, StringComparison.OrdinalIgnoreCase);
        if (!isParticipant)
        {
            return CallOperationResult.NotYourCall;
        }

        if (call.State is CallState.Ended or CallState.Rejected or CallState.TimedOut)
        {
            return CallOperationResult.InvalidState;
        }

        call.State = CallState.Ended;
        ReleaseParticipants(call);
        return CallOperationResult.Ok;
    }
    

    public CallSession? EndAnyActiveCallFor(string username)
    {
        if (!_activeCallByUser.TryGetValue(username, out var callId))
        {
            return null;
        }

        if (!_calls.TryGetValue(callId, out var call))
        {
            return null;
        }

        if (call.State is CallState.Connected or CallState.Ringing or CallState.Calling)
        {
            call.State = CallState.Ended;
        }

        ReleaseParticipants(call);
        return call;
    }


    public bool IsCallConnected(Guid callId)
    {
        return _calls.TryGetValue(callId, out var call) && call.State == CallState.Connected;
    }

    private void ReleaseParticipants(CallSession call)
    {
        _activeCallByUser.TryRemove(call.Caller, out _);
        _activeCallByUser.TryRemove(call.Callee, out _);
    }

    private async Task ExpireIfStillRingingAsync(Guid callId)
    {
        await Task.Delay(RingTimeout);

        if (_calls.TryGetValue(callId, out var call) && call.State == CallState.Ringing)
        {
            call.State = CallState.TimedOut;
            ReleaseParticipants(call);
            OnCallTimedOut?.Invoke(call);
        }
    }


    public event Action<CallSession>? OnCallTimedOut;
}

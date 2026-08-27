// 

using System.Numerics;
using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface IPlayerService
{
    MapLocation GetMapLocation();
    Vector3 GetPlayerPos();
    void SetPlayerPos(Vector3 pos);
    Vector3 GetTorrentPos();
    void SetTorrentPos(Vector3 pos);
    // False on the main menu and during a loading screen. Everything that moves
    // the player must check this first — see GameWorld.IsReady.
    bool IsInGameWorld();
    void SavePos(int index);
    void RestorePos(int index);
    void RestorePos(Position savedPos);

    // RestorePos, but a cross-area warp completes before returning. Use when later
    // steps must run with the player already at the destination.
    void RestorePosBlocking(Position savedPos);
    Position CapturePosition();
    uint GetIgt();
    void MoveToPosition(Position targetPosition);
    nint GetPlayerIns();
    uint GetBlockId();
    void SetHp(int hp);
    int GetCurrentHp();
    int GetMaxHp();
    void SetFullHp();
    void SetRfbs();
    void SetFp(int fp);
    int GetCurrentFp();
    int GetMaxFp();
    void SetSp(int sp);
    int GetCurrentSp();
    float GetSpeed();
    void SetSpeed(float speed);
    void ToggleInfinitePoise(bool isInfinitePoiseEnabled);
    void ToggleDebugFlag(int offset, bool isEnabled, bool needsReminder = false);
    void ToggleNoDamage(bool isNoDamageEnabled);
    void ToggleNoHit(bool isNoHitEnabled);
    void ToggleLockHp(bool isEnabled);
    void ToggleNoRuneGain(bool isNoRuneGainEnabled);
    void ToggleNoRuneArcLoss(bool isNoRuneArcLossEnabled);
    void ToggleNoRuneLoss(bool isNoRuneLossEnabled);
    void ToggleNoTimePassOnDeath(bool isNoTimePassOnDeathEnabled);
    void SetNewGame(int value);
    int GetNewGame();
    void ChangeRunes(int runes);
    int GetRuneLevel();
    Stats GetStats();
    void SetStat(int offset, int newValue);
    long GetHandle();
    void ToggleNoGravity(bool isEnabled);
    void ToggleTorrentNoDeath(bool isEnabled);
    void SetScadu(int value);
    int GetScadu();
    void SetSpiritAsh(int value);
    int GetSpiritAsh();
    int GetCurrentAnimation();
    void ToggleTorrentAnywhere(bool isEnabled);
    bool IsRiding();
    void RefreshFromStorage();
    void ToggleNoRoll(bool isEnabled);
}
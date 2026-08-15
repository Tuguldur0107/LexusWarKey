using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Writes the player's chosen hotkey letters onto their live skills. Each pass reads the
/// command card and, for every detected skill the player has assigned a letter to, writes that letter
/// into the skill's authoritative hotkey cell (the proven duplicate-fix write). The player then
/// presses the letters natively - the app never injects a key at cast time.
///
/// State (which cell now holds which letter) lives here because once a cell is rewritten the resolver
/// can no longer find it by its default letter; the policy is the pure <see cref="SkillWritePlanner"/>.
/// A pass touches the game, so callers run it off the UI thread.</summary>
public sealed class SkillHotkeyWriter
{
    private readonly LiveHotkeyReader _reader;
    private readonly Dictionary<string, (ulong Cell, char Letter)> _written = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private string _lastLog = "";

    public SkillHotkeyWriter(LiveHotkeyReader reader) => _reader = reader;

    public sealed record PassResult(LiveHotkeyReader.Status Status, IReadOnlyList<DetectedSkill> Skills);

    /// <summary>One read-then-write pass. <paramref name="desiredById"/> is the player's assigned
    /// letter per ability id.</summary>
    public PassResult RunPass(IReadOnlyDictionary<string, char> desiredById)
    {
        var (status, card, live) = _reader.ReadDetailed();

        lock (_gate)
        {
            if (status == LiveHotkeyReader.Status.NotRunning)
            {
                _written.Clear();
                Log("pass: war3 not running");
                return new PassResult(status, Array.Empty<DetectedSkill>());
            }
            if (status != LiveHotkeyReader.Status.Ok)
            {
                Log($"pass: {status} (no admin, or no unit selected) - skills idle");
                return new PassResult(status, Array.Empty<DetectedSkill>());
            }

            // A tracked skill leaving the card means the hero/match changed: start fresh.
            var cardIds = card.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            if (_written.Keys.Any(id => !cardIds.Contains(id)))
                _written.Clear();

            // Where each skill's cell is and what letter it holds now: prefer what we wrote, fall back
            // to the live-resolved value (which only exists for skills still on their default letter).
            var cellById = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var currentById = new Dictionary<string, char>(StringComparer.Ordinal);
            foreach (var s in live.Skills)
            {
                cellById[s.Ability.Id] = s.CellAddr;
                currentById[s.Ability.Id] = s.Letter;
            }
            foreach (var (id, wcell) in _written)
            {
                cellById[id] = wcell.Cell;
                currentById[id] = wcell.Letter;
            }

            // De-dupe the card by id (an ability can list twice) and attach cell + current letter.
            var byId = card.GroupBy(a => a.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
            var defaultById = byId.ToDictionary(a => a.Id, a => a.Letter, StringComparer.Ordinal);
            var detected = byId
                .Select(a => (Ability: a,
                              Cell: cellById.GetValueOrDefault(a.Id),
                              Current: currentById.TryGetValue(a.Id, out var c) ? c : a.Letter))
                .ToList();

            // A skill we changed but the player has now un-assigned must be put back to its default
            // letter, not left on the last key we wrote.
            var desired = new Dictionary<string, char>(desiredById, StringComparer.Ordinal);
            foreach (var id in _written.Keys.Where(id => !desired.ContainsKey(id)).ToList())
                if (defaultById.TryGetValue(id, out var def))
                    desired[id] = def;

            var (_, writes) = SkillWritePlanner.Plan(detected, desired);
            foreach (var write in writes)
            {
                var hk = new HotkeyWrite(write.CellAddr, write.DesiredLetter, write.AbilityId, write.CurrentLetter);
                if (_reader.Apply(hk, out var error))
                {
                    currentById[write.AbilityId] = write.DesiredLetter;
                    // Restored to default -> stop tracking it (its cell now matches the default again);
                    // otherwise remember what we wrote.
                    if (defaultById.TryGetValue(write.AbilityId, out var def) && def == write.DesiredLetter)
                        _written.Remove(write.AbilityId);
                    else
                        _written[write.AbilityId] = (write.CellAddr, write.DesiredLetter);
                    DiagnosticLog.Write($"skill write: {write.AbilityId} {write.CurrentLetter}->{write.DesiredLetter}");
                }
                else
                {
                    DiagnosticLog.Write($"skill write failed for {write.AbilityId}: {error}");
                }
            }

            // Re-plan for the display with the post-write letters.
            var updated = detected
                .Select(d => (d.Ability, d.Cell, Current: currentById.TryGetValue(d.Ability.Id, out var c) ? c : d.Current))
                .ToList();
            var (rows, _) = SkillWritePlanner.Plan(updated, desiredById);
            Log("pass: [" + string.Join(" ", rows.Select(r => $"{r.CurrentLetter}:{r.Ability.Name}")) + "]");
            return new PassResult(status, rows);
        }
    }

    public void Clear()
    {
        lock (_gate)
            _written.Clear();
    }

    private void Log(string message)
    {
        if (message == _lastLog)
            return;
        _lastLog = message;
        DiagnosticLog.Write(message);
    }
}

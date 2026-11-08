using Gatekeeper.Mods;

internal static class ExpansionMusicChecks
{
    internal static void Run(Action<bool, string> check)
    {
        bool valid = true, alternating = true, completeBags = true, noRepeat = true;
        for (var seed = 0; seed < 128; seed++)
        {
            var rotation = new ExpansionPlaylist(new Random(seed));
            var turns = Enumerable.Range(0, 980).Select(_ => rotation.Next()).ToArray();
            valid &= turns.All(t => t >= -1 && t < 7);
            alternating &= turns.Zip(turns.Skip(1)).All(p => (p.First == -1) != (p.Second == -1));
            var songs = turns.Where(t => t >= 0).ToArray();
            completeBags &= songs.Chunk(7).All(b => b.Length == 7 && b.Distinct().Count() == 7);
            noRepeat &= songs.Zip(songs.Skip(1)).All(p => p.First != p.Second);
        }
        check(valid, "Music rotation returned an unavailable song.");
        check(alternating, "The original soundtrack lost its regular turns in the expansion music rotation.");
        check(completeBags, "One of the seven replacement songs could be skipped before the rotation repeats.");
        check(noRepeat, "An added song immediately repeated across shuffle boundaries.");
    }
}

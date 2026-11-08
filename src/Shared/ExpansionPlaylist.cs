namespace Gatekeeper.Mods;

// Native soundtrack turns alternate with a shuffled bag of the seven replacement songs.
// Keep the bag across level changes so short stages cannot starve the later songs.
internal sealed class ExpansionPlaylist
{
    private readonly Random random;
    private readonly int[] songs = { 0, 1, 2, 3, 4, 5, 6 };
    private int position = 7, last = -1;
    private bool expansionNext;

    internal ExpansionPlaylist(Random random)
    { this.random = random; expansionNext = random.Next(2) == 0; }

    internal int Next()
    {
        expansionNext = !expansionNext;
        if (expansionNext) return -1;
        if (position == songs.Length)
        {
            for (var i = songs.Length - 1; i > 0; i--)
            { var j = random.Next(i + 1); (songs[i], songs[j]) = (songs[j], songs[i]); }
            if (songs[0] == last) (songs[0], songs[1]) = (songs[1], songs[0]);
            position = 0;
        }
        return last = songs[position++];
    }
}

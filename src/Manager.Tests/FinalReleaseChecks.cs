using Gatekeeper.Mods;
using Gatekeeper.ModManager;

internal static class FinalReleaseChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(!ExpansionKit.Profile().Metalheads25,"The release re-enabled expanded Metalheads.");
        check(new BalanceProfile{Metalheads25=true}.CustomConfig().Contains("[Metalheads]\nEnabled = false"),"Old profiles can re-enable the removed Metalhead override.");
        foreach(var id in new[]{11,29,52})
        {
            check(ItemCapRules.Allow(id,24,10)==1,"A bulk item award exceeded 25.");
            check(ItemCapRules.Allow(id,25,1)==0 && ItemCapRules.Allow(id,30,1)==0,"A capped/legacy inventory accepted another limited item.");
            check(ItemCapRules.Allow(id,0,int.MaxValue)==25 && ItemCapRules.Allow(id,int.MaxValue,int.MaxValue)==0,"Item cap overflowed for a malformed large award.");
        }
        check(ItemCapRules.Allow(95,1,30)==30,"The new offensive item caps changed native Halfmetal Head behavior.");
        check(ForgeRules.BaseMoveSpeed==9 && ForgeRules.BaseHealth==250 && ForgeRules.BaseRegeneration==2,"Forge's release stats are incorrect.");
        check(Math.Abs(ForgeRules.RivetPushDistance/1.8f-1.15f)<.00001,"Rivet knockback did not increase by 15%.");
        check(ExchangeRules.Markup(200)==253 && ExchangeRules.SalePrice(200)==100,"The extra purchase penalty changed sale payouts.");
        var celebration=new JackpotCelebration();var round=Guid.NewGuid().ToString("N");
        celebration.Begin(round,0);
        check(!celebration.Claim(round,false) && celebration.Claim(round,true) && !celebration.Claim(round,true),"A losing or repeated slot result emitted confetti.");
        celebration.Reset();celebration.Begin(round,SlotRules.SpinSeconds);
        check(!celebration.Claim(round,true),"A late join replayed jackpot confetti.");
        celebration.Reset();celebration.Begin(round,1);
        check(!celebration.Claim(Guid.NewGuid().ToString("N"),true) && celebration.Claim(round,true),"Another round consumed a jackpot celebration.");
        var playlist=new ExpansionPlaylist(new Random(153));var bag=new List<int>();var previous=-2;var previousSong=-1;var complete=0;
        var alternating=true;var valid=true;var completeBags=true;
        for(var i=0;i<700;i++)
        {
            var current=playlist.Next();
            if(i>0)alternating&=(current==-1)!=(previous==-1);previous=current;
            if(current<0)continue;
            valid&=current<7 && current!=previousSong;previousSong=current;bag.Add(current);
            if(bag.Count!=7)continue;
            completeBags&=bag.Order().SequenceEqual(Enumerable.Range(0,7));bag.Clear();complete++;
        }
        check(complete==50,"Shuffle testing did not cover all 50 seven-song bags.");
        check(alternating && valid && completeBags,"The music rotation repeated/omitted songs or lost native soundtrack turns.");
    }
}

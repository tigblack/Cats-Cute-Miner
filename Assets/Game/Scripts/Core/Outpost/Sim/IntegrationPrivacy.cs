using System;

namespace CatMiners.Core.Outpost
{
    public enum NeutralAge { Unselected, Unknown, Under13, Age13To15, Age16OrOlder }
    [Serializable]
    public sealed class IntegrationPrivacy
    {
        public NeutralAge Age;
        public bool ChoicesMade;
        public bool AppMetrica, Adjust, Meta, Ads;
        public bool Protected => Age != NeutralAge.Age16OrOlder;
        public bool MayInitialize(bool choice) => !Protected && ChoicesMade && choice;
        public void ChooseAge(NeutralAge age)
        {
            Age = Enum.IsDefined(typeof(NeutralAge), age) && age != NeutralAge.Unselected ? age : NeutralAge.Unknown;
            ChoicesMade = Protected;
            AppMetrica = Adjust = Meta = Ads = false;
        }
    }
    [Serializable]
    public sealed class RewardedOffer
    {
        public string Id, RunId, ItemId, Gold;
        public long OfferedUtcMs;
        public bool Earned;
    }
}

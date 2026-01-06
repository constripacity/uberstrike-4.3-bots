namespace UberStrikeBot
{
    [System.Serializable]
    public class BotMetrics
    {
        public int ShotsFired;
        public int ShotsHit;
        public float DamageDealt;
        public float DamageTaken;
        public int Kills;
        public int Deaths;
        public float SurvivalTime;
        public int SuccessfulFlanks;
        public int CoverUtilizationCount;
        
        public float EngagementStartTime;
        public int EngagementsCount;
        public float TotalReactionTime; // Sum of reaction times to average later

        public float Accuracy { get { return ShotsFired > 0 ? (float)ShotsHit / ShotsFired : 0f; } }
        public float AverageReactionTime { get { return EngagementsCount > 0 ? TotalReactionTime / EngagementsCount : 0f; } }
        public float DamageDealtPerMinute { get { return SurvivalTime > 0 ? (DamageDealt / SurvivalTime) * 60f : 0f; } }

        public void Reset()
        {
            ShotsFired = 0;
            ShotsHit = 0;
            DamageDealt = 0;
            DamageTaken = 0;
            Kills = 0;
            Deaths = 0;
            SurvivalTime = 0;
            SuccessfulFlanks = 0;
            CoverUtilizationCount = 0;
            EngagementsCount = 0;
            TotalReactionTime = 0;
        }
    }
}

namespace TidalNexus.StandaloneServer.Core
{

    public enum TradeRefusal
    {
        None = 0,

        BadAmount,

        NotTraded,

        TooMany,
    }

    public readonly struct TradeQuote
    {
        private TradeQuote(long unitPrice, long total, TradeRefusal refusal)
        {
            UnitPrice = unitPrice;
            Total = total;
            Refusal = refusal;
        }

        public static TradeQuote Refused(TradeRefusal reason) =>
            new TradeQuote(0, 0, reason == TradeRefusal.None ? TradeRefusal.NotTraded : reason);

        public static TradeQuote Priced(long unitPrice, long total) =>
            new TradeQuote(unitPrice, total, TradeRefusal.None);

        public long UnitPrice { get; }

        public long Total { get; }

        public TradeRefusal Refusal { get; }

        public bool Allowed => Refusal == TradeRefusal.None;
    }

    public static class TradePricing
    {

        public const float StationRange = 15f;

        public static bool InRange(float distance, float range = StationRange)
        {

            return distance >= 0f && distance <= range;
        }

        public static long BuyUnitPrice(bool stationStocksIt, int rowPrice, int cataloguePrice)
        {
            if (!stationStocksIt)
            {
                return 0;
            }

            if (rowPrice > 0)
            {
                return rowPrice;
            }

            return cataloguePrice > 0 ? cataloguePrice : 0;
        }

        public static long SellUnitPrice(int stationPrice, float sellRate)
        {

            if (stationPrice <= 0 || !(sellRate > 0f) || float.IsInfinity(sellRate))
            {
                return 0;
            }

            double scaled = stationPrice * (double)sellRate;
            return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        }

        public static TradeQuote Quote(long unitPrice, int amount)
        {
            if (amount <= 0)
            {
                return TradeQuote.Refused(TradeRefusal.BadAmount);
            }

            if (unitPrice <= 0)
            {
                return TradeQuote.Refused(TradeRefusal.NotTraded);
            }

            if (unitPrice > long.MaxValue / amount)
            {
                return TradeQuote.Refused(TradeRefusal.TooMany);
            }

            return TradeQuote.Priced(unitPrice, unitPrice * amount);
        }

        public static TradeQuote QuoteBuy(
            bool stationStocksIt, int rowPrice, int cataloguePrice, int amount) =>
            Quote(BuyUnitPrice(stationStocksIt, rowPrice, cataloguePrice), amount);

        public static TradeQuote QuoteSell(int stationPrice, float sellRate, int amount) =>
            Quote(SellUnitPrice(stationPrice, sellRate), amount);
    }
}

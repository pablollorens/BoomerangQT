using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public static class Extensions
    {
        public static Side Invert(this Side side)
        {
            return side == Side.Buy ? Side.Sell : Side.Buy;
        }

        public static bool IsFilled(this IOrder order)
        {
            return order.Status == OrderStatus.Filled;
        }
    }
}

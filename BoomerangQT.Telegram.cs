using System;
using System.Net.Http;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        [InputParameter("Enable Telegram Notifications", 300)]
        public bool enableTelegramlNotification = false;

        [InputParameter("Telegram Bot Token", 301)]
        public string tgBotToekn = "botToken";

        [InputParameter("Telegram Message", 303)]
        public string tgMessage = "Closed trade in {Symbol} with PnL: {PnL}, Max Drawdown: {MaxDrawdown}";

        [InputParameter("Telegram Chat ID", 304)]
        public string tgChatId = "chatId";

        private async Task SendTradeClosedTelegramMsg(string symbol, double pnl, double maxDrawdown)
        {
            if (!enableTelegramlNotification)
                return;

            string message = tgMessage
                .Replace("{Symbol}", symbol)
                .Replace("{PnL}", pnl.ToString("F2"))
                .Replace("{MaxDrawdown}", maxDrawdown.ToString("F2"));

            await SendTelegramMessageAsync(message);
        }

        private async Task SendTelegramMessageAsync(string msg)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"https://api.telegram.org/bot{tgBotToekn}/sendMessage?chat_id={tgChatId}&text={Uri.EscapeDataString(msg)}";
                    HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Telegram Error sending message: {response.StatusCode}", StrategyLoggingLevel.Error);
                    }
                }
            }

            catch (Exception ex)
            {
                Log($"Error sending telegram: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }
    }
}
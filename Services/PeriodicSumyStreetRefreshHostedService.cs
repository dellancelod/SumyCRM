namespace SumyCRM.Services
{
    public class PeriodicSumyStreetRefreshHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PeriodicSumyStreetRefreshHostedService> _log;

        // Change to what you want: 12h, 7d, etc.
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        public PeriodicSumyStreetRefreshHostedService(IServiceScopeFactory scopeFactory, ILogger<PeriodicSumyStreetRefreshHostedService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run once shortly after start
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ISumyStreetImportService>();

                    var count = await svc.RefreshAsync(stoppingToken);
                    _log.LogInformation("Street refresh finished. Count={Count}", count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Street refresh crashed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}

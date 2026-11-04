using System;
using RRC.Abstractions;

namespace RRC.Student
{
    public class MyRequestPolicy : IRequestPolicy
    {
        private readonly object _lock = new();

        private const int MaxAttempts = 3;
        private const double MinTimeoutMs = 200.0;
        private const double MaxTimeoutMs = 60000.0;

        private double _emaDurationMs = 350.0;
        private double _emaDeviationMs = 100.0;
        private int _consecutiveTimeouts = 0;

        public TimeSpan GetTimeout(RequestContext context, Random random)
        {
            lock (_lock)
            {
                double baseTimeout = _emaDurationMs + (2.2 * _emaDeviationMs);

                if (_consecutiveTimeouts > 3)
                {
                    baseTimeout *= 1.3;
                }

                double clampedMs = Math.Clamp(baseTimeout, MinTimeoutMs, MaxTimeoutMs);
                return TimeSpan.FromMilliseconds(clampedMs);
            }
        }

        public RetryDecision OnResult(RequestObservation observation, Random random)
        {
            lock (_lock)
            {
                bool isSuccess = observation.Outcome == RequestOutcome.Success;

                if (isSuccess)
                {
                    _consecutiveTimeouts = 0;

                    double duration = observation.Elapsed.TotalMilliseconds;
                    double delta = Math.Abs(duration - _emaDurationMs);
                    _emaDurationMs = (0.2 * duration) + (0.8 * _emaDurationMs);
                    _emaDeviationMs = (0.2 * delta) + (0.8 * _emaDeviationMs);

                    return RetryDecision.NoRetry();
                }

                if (observation.Attempt >= MaxAttempts)
                {
                    return RetryDecision.NoRetry();
                }

                bool isTimeout = observation.Outcome == RequestOutcome.Timeout;
                int currentMaxAttempts = MaxAttempts;
                if (isTimeout)
                {
                    _consecutiveTimeouts++;
                }
                if (_consecutiveTimeouts > 4)
                {
                    currentMaxAttempts = 1;
                }
                else if (_consecutiveTimeouts > 2)
                {
                    currentMaxAttempts = 2;
                }
                if (observation.Attempt >= currentMaxAttempts)
                {
                    return RetryDecision.NoRetry();
                }

                double baseDelayMs = isTimeout ? 400.0 : 150.0;

                baseDelayMs *= Math.Pow(2.2, observation.Attempt - 1);

                if (_consecutiveTimeouts > 3)
                {
                    baseDelayMs *= 5;
                }

                double jitter = 0.5 + random.NextDouble();
                double finalDelayMs = baseDelayMs * jitter;

                finalDelayMs = Math.Clamp(finalDelayMs, 50.0, 40000.0);

                return RetryDecision.RetryAfter(TimeSpan.FromMilliseconds(finalDelayMs));
            }
        }
    }
}
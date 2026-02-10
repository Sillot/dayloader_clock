using DayloaderClock.Models;
using DayloaderClock.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DayloaderClock.Tests;

/// <summary>
/// Helper to build a <see cref="SessionService"/> with controllable time and mocked storage.
/// </summary>
internal sealed class SessionServiceBuilder
{
    private AppSettings _settings = new();
    private IStorageService? _storage;
    private SessionStore _store = new();
    private FakeTimeProvider? _timeProvider;

    public SessionServiceBuilder WithSettings(AppSettings settings)
    {
        _settings = settings;
        return this;
    }

    public SessionServiceBuilder WithSettings(Action<AppSettings> configure)
    {
        configure(_settings);
        return this;
    }

    public SessionServiceBuilder WithStore(SessionStore store)
    {
        _store = store;
        return this;
    }

    public SessionServiceBuilder WithTime(DateTimeOffset startTime)
    {
        _timeProvider = new FakeTimeProvider(startTime);
        return this;
    }

    public SessionServiceBuilder WithTimeProvider(FakeTimeProvider provider)
    {
        _timeProvider = provider;
        return this;
    }

    public (SessionService Service, FakeTimeProvider Time, IStorageService Storage) Build()
    {
        var time = _timeProvider ?? new FakeTimeProvider(
            new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)));

        var storage = _storage ?? Substitute.For<IStorageService>();
        storage.LoadSessions().Returns(_store);

        var service = new SessionService(_settings, storage, time);
        return (service, time, storage);
    }
}

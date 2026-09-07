namespace Gpui;

/// <summary>Application-owned place vocabulary. Never crosses the native ABI.</summary>
public enum PlaceTag
{
    Beach,
    Mountain,
    City,
}

/// <summary>One destination card. Identity is <see cref="Id"/> (never zero).</summary>
public sealed record Destination(
    int Id,
    string Name,
    string Country,
    PlaceTag Tag,
    string Blurb,
    int Likes,
    bool Liked
);

/// <summary>One feed entry. Identity is <see cref="Id"/> (never zero).</summary>
public sealed record FeedEntry(
    long Id,
    string Author,
    string Text,
    int DestId,
    int Likes,
    bool Liked,
    int MinutesAgo
);

/// <summary>One trip plan. Identity is <see cref="Id"/>.</summary>
public sealed record Trip(
    int Id,
    string Title,
    int[] DestIds,
    bool[] Days,
    int Rating
);

/// <summary>
/// In-memory document for the sample. Mutations that must rerender subscribers go
/// through <see cref="Notify"/>; silent mutations (feed likes) skip it so the owning
/// View can use targeted row refresh instead of evicting every batch.
/// </summary>
public sealed class TravelStore
{
    private long _nextEntry = 1;
    private int _nextTrip = 100;
    private ulong _revision = 1;
    private event Action? Changed;

    public List<Destination> Destinations { get; } = [];
    public List<FeedEntry> Entries { get; } = [];
    public List<Trip> Trips { get; } = [];
    public string ProfileName { get; private set; } = "Aiko Tanaka";
    public string ProfileBio { get; private set; } = "Chasing sunrises, one trail at a time.";
    public bool Notifications { get; private set; } = true;
    public float GoalKm { get; private set; } = 120;
    public float WalkedKm { get; private set; } = 86;
    public ulong Revision => _revision;

    public TravelStore()
    {
        Seed();
    }

    public IDisposable Subscribe(Action callback)
    {
        Changed += callback;
        return new Subscription(this, callback);
    }

    private void Notify() => Changed?.Invoke();

    private void Bump() => _revision++;

    private sealed class Subscription(TravelStore store, Action callback) : IDisposable
    {
        public void Dispose() => store.Changed -= callback;
    }

    public Destination? FindDest(int id) => Destinations.Find(d => d.Id == id);

    public FeedEntry? FindEntry(long id) => Entries.Find(e => e.Id == id);

    public Trip? FindTrip(int id) => Trips.Find(t => t.Id == id);

    public int EntryIndex(long id)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Silent like: no revision bump, no notify; caller refreshes the row range.</summary>
    public void ToggleEntryLike(long id)
    {
        var index = EntryIndex(id);
        if (index < 0)
        {
            return;
        }
        var entry = Entries[index];
        var liked = !entry.Liked;
        Entries[index] = entry with { Liked = liked, Likes = entry.Likes + (liked ? 1 : -1) };
    }

    public void ToggleDestLike(int id)
    {
        var index = Destinations.FindIndex(d => d.Id == id);
        if (index < 0)
        {
            return;
        }
        var dest = Destinations[index];
        var liked = !dest.Liked;
        Destinations[index] = dest with { Liked = liked, Likes = dest.Likes + (liked ? 1 : -1) };
        Bump();
        Notify();
    }

    public void AddEntry(string author, string text, int destId)
    {
        Entries.Insert(
            0,
            new FeedEntry(_nextEntry++, author, text, destId, 0, false, 0)
        );
        Bump();
        Notify();
    }

    public void ToggleDay(int tripId, int day)
    {
        var index = Trips.FindIndex(t => t.Id == tripId);
        if (index < 0 || (uint)day >= 7u)
        {
            return;
        }
        var trip = Trips[index];
        var days = (bool[])trip.Days.Clone();
        days[day] = !days[day];
        Trips[index] = trip with { Days = days };
        WalkedKm += days[day] ? 12 : -12;
        Bump();
        Notify();
    }

    public void SetRating(int tripId, int stars)
    {
        var index = Trips.FindIndex(t => t.Id == tripId);
        if (index < 0)
        {
            return;
        }
        Trips[index] = Trips[index] with { Rating = Math.Clamp(stars, 0, 5) };
        Bump();
        Notify();
    }

    public void AddTrip(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }
        Trips.Add(new Trip(_nextTrip++, title.Trim(), [Destinations[0].Id], new bool[7], 0));
        Bump();
        Notify();
    }

    public void AddDestToTrip(int destId)
    {
        if (Trips.Count == 0 || FindDest(destId) is null)
        {
            return;
        }
        var trip = Trips[0];
        if (Array.IndexOf(trip.DestIds, destId) >= 0)
        {
            return;
        }
        var ids = new int[trip.DestIds.Length + 1];
        Array.Copy(trip.DestIds, ids, trip.DestIds.Length);
        ids[^1] = destId;
        Trips[0] = trip with { DestIds = ids };
        Bump();
        Notify();
    }

    public void SetProfile(string name, string bio)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            ProfileName = name.Trim();
        }
        ProfileBio = bio.Trim();
        Bump();
        Notify();
    }

    public void SetNotifications(bool value)
    {
        Notifications = value;
        Bump();
        Notify();
    }

    public void SetGoal(float km)
    {
        GoalKm = Math.Clamp(km, 20, 400);
        Bump();
        Notify();
    }

    public void Reset()
    {
        Destinations.Clear();
        Entries.Clear();
        Trips.Clear();
        ProfileName = "Aiko Tanaka";
        ProfileBio = "Chasing sunrises, one trail at a time.";
        Notifications = true;
        GoalKm = 120;
        WalkedKm = 86;
        Seed();
        Bump();
        Notify();
    }

    /// <summary>Pure feed filter for memo calculations. Allocates only on input change.</summary>
    public static List<FeedEntry> ApplyFilter(ExploreFilter filter)
    {
        var store = filter.Store;
        var result = new List<FeedEntry>(store.Entries.Count);
        foreach (var entry in store.Entries)
        {
            if (filter.Chip != 0)
            {
                var dest = store.FindDest(entry.DestId);
                if (dest is null || (int)dest.Tag != filter.Chip - 1)
                {
                    continue;
                }
            }
            if (filter.StoryDest >= 0 && entry.DestId != filter.StoryDest)
            {
                continue;
            }
            if (!filter.Query.IsEmpty
                && !entry.Text.Contains(filter.Query.Text, StringComparison.OrdinalIgnoreCase)
                && !entry.Author.Contains(filter.Query.Text, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            result.Add(entry);
        }
        return result;
    }

    private void Seed()
    {
        Destinations.Add(new(0, "Bali", "Indonesia", PlaceTag.Beach, "Temple mornings, surf afternoons, waterfall chases in between.", 214, false));
        Destinations.Add(new(1, "Kyoto", "Japan", PlaceTag.City, "Maple lanes, quiet shrines, and the best kissaten coffee.", 189, true));
        Destinations.Add(new(2, "Zermatt", "Switzerland", PlaceTag.Mountain, "Matterhorn views and trails above the clouds.", 167, false));
        Destinations.Add(new(3, "Marrakech", "Morocco", PlaceTag.City, "Souks, spices, and rooftop sunsets over the medina.", 142, false));
        Destinations.Add(new(4, "Banff", "Canada", PlaceTag.Mountain, "Turquoise lakes and ridgelines worth every switchback.", 158, false));
        Destinations.Add(new(5, "Santorini", "Greece", PlaceTag.Beach, "White walls, blue domes, and slow Aegean evenings.", 201, true));
        Destinations.Add(new(6, "Patagonia", "Chile", PlaceTag.Mountain, "Granite towers and wind that rewrites your plans.", 98, false));
        Destinations.Add(new(7, "Hanoi", "Vietnam", PlaceTag.City, "Old-quarter chaos, egg coffee, and midnight pho.", 121, false));

        var authors = new[] { "Aiko", "Ben", "Chloe", "Dev", "Eri", "Farah" };
        var notes = new[]
        {
            "Sunrise from the crater rim. No filter could survive this.",
            "Found the alley with the lanterns. Stayed for three hours.",
            "Trail day: 18km, two blisters, one perfect lake.",
            "The night market dumplings live up to the hype.",
            "Ferry delayed, viewpoint empty. Sometimes luck wins.",
            "Local bus, wrong stop, best viewpoint of the trip.",
        };
        var random = new Random(7);
        for (var i = 0; i < 24; i++)
        {
            var dest = i % Destinations.Count;
            Destinations[dest] = Destinations[dest] with { Likes = Destinations[dest].Likes + (i % 5) };
            Entries.Add(
                new FeedEntry(
                    _nextEntry++,
                    authors[random.Next(authors.Length)],
                    $"{notes[i % notes.Length]} #{Destinations[dest].Name}",
                    dest,
                    3 + random.Next(40),
                    false,
                    12 + i * 37
                )
            );
        }
        Trips.Add(new Trip(1, "Alpine summer", [2, 4], [true, true, false, false, false, false, false], 4));
        Trips.Add(new Trip(2, "Island hopping", [0, 5], [true, false, false, false, false, false, false], 5));
        Trips.Add(new Trip(3, "City lights", [1, 3, 7], [false, false, false, false, false, false, false], 3));
    }
}

/// <summary>Equatable search text for memo inputs.</summary>
public readonly record struct BoardQuery(string Text)
{
    public bool IsEmpty => string.IsNullOrEmpty(Text);
}

/// <summary>Memo input for the explore feed: store plus all local filter state.</summary>
public readonly record struct ExploreFilter(
    TravelStore Store,
    ulong StoreRevision,
    BoardQuery Query,
    int Chip,
    int StoryDest
);

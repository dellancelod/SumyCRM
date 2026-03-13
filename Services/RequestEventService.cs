using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Services
{
    public class RequestEventService : IRequestEventService
    {
        private readonly AppDbContext _db;
        private readonly IGeocodingService _geocodingService;

        public RequestEventService(AppDbContext db, IGeocodingService geocodingService)
        {
            _db = db;
            _geocodingService = geocodingService;
        }

        public async Task SyncFromRequestAsync(Request request, CancellationToken ct = default)
        {
            if (request == null)
                return;

            // если заявка выполнена — удаляем событие
            if (request.IsCompleted)
            {
                var existingEvent = await _db.Events
                    .FirstOrDefaultAsync(x => x.RequestId == request.Id, ct);

                if (existingEvent != null)
                {
                    _db.Events.Remove(existingEvent);
                    await _db.SaveChangesAsync(ct);
                }

                return;
            }

            var categoryName = request.Category?.Title;

            if (string.IsNullOrWhiteSpace(categoryName) && request.CategoryId != Guid.Empty)
            {
                categoryName = await _db.Categories
                    .Where(x => x.Id == request.CategoryId)
                    .Select(x => x.Title)
                    .FirstOrDefaultAsync(ct);
            }

            var streetName = ExtractStreetName(request.Address);
            var shortAddress = ExtractStreetAndHouse(request.Address);

            double? lat = null;
            double? lon = null;

            var geoQuery = !string.IsNullOrWhiteSpace(shortAddress)
                ? shortAddress
                : streetName;

            if (!string.IsNullOrWhiteSpace(geoQuery))
            {
                var geo = await _geocodingService.GeocodeAsync(geoQuery, ct);
                if (geo != null)
                {
                    lat = geo.Value.lat;
                    lon = geo.Value.lon;
                }
            }

            var entity = await _db.Events
                .FirstOrDefaultAsync(x => x.RequestId == request.Id, ct);

            if (entity == null)
            {
                entity = new Event
                {
                    RequestId = request.Id,
                    DateAdded = request.DateAdded
                };

                _db.Events.Add(entity);
            }

            entity.CategoryName = categoryName ?? "";
            entity.Address = shortAddress ?? streetName ?? request.Address ?? "";
            entity.Text = request.Subcategory ?? "";
            entity.IsCompleted = false;
            entity.Latitude = lat;
            entity.Longitude = lon;

            await _db.SaveChangesAsync(ct);
        }

        // --------------------------
        // STREET + HOUSE
        // --------------------------

        private static string ExtractStreetAndHouse(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return "";

            var s = NormalizeAddress(address);

            var patterns = new[]
            {
                @"(?ix)
                \b(?<prefix>вул\.?|вулиця|просп\.?|проспект|пров\.?|провулок|пл\.?|площа|майдан|наб\.?|набережна|шосе|бульв\.?|бульвар)\s+
                (?<street>[^\d,]+?)
                [,\s]+
                (?<house>\d+[A-Za-zА-Яа-яІіЇїЄєҐґ\-\/]*)",

                @"(?ix)
                ^\s*
                (?<street>[A-Za-zА-Яа-яІіЇїЄєҐґ0-9'\- ]+?)
                \s*,\s*
                (?<house>\d+[A-Za-zА-Яа-яІіЇїЄєҐґ\-\/]*)",

                @"(?ix)
                ^\s*
                (?<street>[A-Za-zА-Яа-яІіЇїЄєҐґ'\- ]+?)
                \s+
                (?<house>\d+[A-Za-zА-Яа-яІіЇїЄєҐґ\-\/]*)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(s, pattern, RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                var prefix = match.Groups["prefix"].Success
                    ? match.Groups["prefix"].Value.Trim().TrimEnd('.')
                    : "";

                var street = match.Groups["street"].Value.Trim().Trim(',', '.');
                var house = match.Groups["house"].Value.Trim().Trim(',', '.');

                street = CleanupStreetTail(street);

                if (string.IsNullOrWhiteSpace(street))
                    continue;

                return string.IsNullOrWhiteSpace(prefix)
                    ? $"{street} {house}".Trim()
                    : $"{prefix} {street} {house}".Trim();
            }

            return ExtractStreetName(address);
        }

        // --------------------------
        // STREET ONLY
        // --------------------------

        private static string ExtractStreetName(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return "";

            var s = NormalizeAddress(address);

            var patterns = new[]
            {
                @"(?i)\b(?<prefix>вул\.?|вулиця)\s+(?<street>[^\.,\d]+)",
                @"(?i)\b(?<prefix>просп\.?|проспект)\s+(?<street>[^\.,\d]+)",
                @"(?i)\b(?<prefix>пров\.?|провулок)\s+(?<street>[^\.,\d]+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(s, pattern, RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                var prefix = match.Groups["prefix"].Value.Trim().TrimEnd('.');
                var street = match.Groups["street"].Value.Trim();

                street = CleanupStreetTail(street);

                if (!string.IsNullOrWhiteSpace(street))
                    return $"{prefix} {street}";
            }

            return "";
        }

        private static string CleanupStreetTail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var s = value;

            s = Regex.Replace(
                s,
                @"(?i)\b(кв\.?|квартира|корп\.?|корпус|під'?їзд|поверх)\b.*$",
                "",
                RegexOptions.CultureInvariant);

            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = s.Trim(',', '.');

            return s;
        }

        private static string NormalizeAddress(string address)
        {
            var s = address.Trim();

            s = s.Replace("“", "\"")
                 .Replace("”", "\"")
                 .Replace("’", "'")
                 .Replace("`", "'");

            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }
    }
}
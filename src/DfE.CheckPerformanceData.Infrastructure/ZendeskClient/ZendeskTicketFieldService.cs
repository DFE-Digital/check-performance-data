using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

/// <summary>
/// Service for resolving Zendesk ticket field IDs by name.
/// Tries configuration first, falls back to querying the Zendesk API.
/// 
/// Registered as Singleton with thread-safe in-memory caching.
/// In a load-balanced environment, each instance maintains its own cache,
/// but the singleton scope ensures the cache is shared across all requests
/// within that instance (vs scoped where each request gets a fresh cache).
/// </summary>
public sealed class ZendeskTicketFieldService : IZendeskTicketFieldService
{
    private readonly ZendeskTicketFieldSettings _settings;
    private readonly IZendeskApi _zendeskApi;
    private readonly ILogger<ZendeskTicketFieldService> _logger;
    
    // Thread-safe cache for field ID lookups
    private readonly object _cacheLock = new object();
    private readonly Dictionary<string, long?> _fieldIdCache = new Dictionary<string, long?>();

    public ZendeskTicketFieldService(
        ZendeskTicketFieldSettings settings,
        IZendeskApi zendeskApi,
        ILogger<ZendeskTicketFieldService> logger)
    {
        _settings = settings;
        _zendeskApi = zendeskApi;
        _logger = logger;
    }

    public long? GetFieldIdFromConfig(string fieldName)
    {
        return _settings.GetFieldIdByName(fieldName);
    }

    public List<CustomFieldDto> GetCustomFieldsFromConfig()
    {
        var fields = new List<CustomFieldDto>();

        foreach (var (name, id) in _settings.GetAllFieldIds())
        {
            if (id.HasValue)
            {
                fields.Add(new CustomFieldDto { Id = id.Value, Value = name });
            }
        }

        return fields;
    }

    public List<string> GetAllFieldNames()
    {
        return ZendeskTicketFieldConstants.GetAllFieldNames();
    }

    public async Task<long?> GetFieldIdAsync(string fieldName)
    {
        // Check cache first (thread-safe read)
        lock (_cacheLock)
        {
            if (_fieldIdCache.TryGetValue(fieldName, out var cachedId))
            {
                return cachedId;
            }
        }

        // Try configuration first
        var configId = _settings.GetFieldIdByName(fieldName);
        if (configId.HasValue)
        {
            lock (_cacheLock)
            {
                _fieldIdCache[fieldName] = configId;
            }
            return configId;
        }

        // Fall back to querying Zendesk API
        try
        {
            _logger.LogInformation("Field ID for '{FieldName}' not found in configuration, querying Zendesk API.", fieldName);
            var response = await _zendeskApi.GetTicketFields();

            var field = response.TicketFields.FirstOrDefault(f => f.Title == fieldName);
            if (field != null)
            {
                lock (_cacheLock)
                {
                    _fieldIdCache[fieldName] = field.Id;
                }
                _logger.LogInformation("Found field ID {Id} for '{FieldName}' via Zendesk API.", field.Id, fieldName);
                return field.Id;
            }

            _logger.LogWarning("Field '{FieldName}' not found in Zendesk API.", fieldName);
            lock (_cacheLock)
            {
                _fieldIdCache[fieldName] = null;
            }
            return null;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to query Zendesk API for field '{FieldName}'.", fieldName);
            throw;
        }
    }

    public string? GetOptionValue(string fieldName, string optionName)
    {
        return ZendeskTicketFieldOptions.GetOptionValue(fieldName, optionName);
    }
}
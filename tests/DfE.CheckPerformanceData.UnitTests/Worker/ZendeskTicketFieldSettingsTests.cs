using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

/// <summary>
/// A field id that is in the settings class but not in the name lookup is never sent. These
/// tests keep the property list, the name lookup and the name list in step.
/// </summary>
public sealed class ZendeskTicketFieldSettingsTests
{
    [Fact]
    public void Each_field_id_setting_can_be_found_by_its_field_name()
    {
        var settings = new ZendeskTicketFieldSettings();
        var next = 1000L;
        var idProperties = typeof(ZendeskTicketFieldSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(long?)).ToList();
        foreach (var property in idProperties)
            property.SetValue(settings, next++);

        var byName = settings.GetAllFieldIds();

        Assert.Equal(idProperties.Count, byName.Count);
        Assert.Equal(idProperties.Count, byName.Select(f => f.Id).Distinct().Count());
        foreach (var (name, id) in byName)
            Assert.Equal(id, settings.GetFieldIdByName(name));
        Assert.Equal(byName.Select(f => f.Name).Order(), ZendeskTicketFieldConstants.GetAllFieldNames().Order());
    }
}

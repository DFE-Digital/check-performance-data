using System;
using System.Collections.Generic;
using System.Linq;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    /// <summary>
    /// Configuration settings for Zendesk ticket field IDs.
    /// This allows field IDs to be configured via appsettings.json or user secrets,
    /// keeping sensitive field identifiers out of source code.
    /// </summary>
    public class ZendeskTicketFieldSettings
    {
        public const string SectionName = "ZendeskTicketFields";

        public long? TypeOfOrganisationId { get; set; }
        public long? DfeEstablishmentNumberId { get; set; }
        public long? UpnId { get; set; }
        public long? SchoolUrnId { get; set; }
        public long? LdsMatchedPupilIdId { get; set; }
        public long? DateOfBirthCypmdId { get; set; }
        public long? AdmissionDateId { get; set; }
        public long? DecisionStatusId { get; set; }
        public long? CorrectionTypeId { get; set; }
        public long? CycleYearId { get; set; }
        public long? CycleMonthId { get; set; }
        public long? KeyStageId { get; set; }
        public long? SurnameCypmdId { get; set; }
        public long? ForenameCypmdId { get; set; }
        public long? SexId { get; set; }
        public long? CorrectionReason31Id { get; set; }
        public long? ReasonForRemovalId { get; set; }
        public long? CypmdId { get; set; }

        /// <summary>
        /// Gets the field ID by field name.
        /// </summary>
        public long? GetFieldIdByName(string fieldName)
        {
            return fieldName switch
            {
                ZendeskTicketFieldConstants.TypeOfOrganisationName => TypeOfOrganisationId,
                ZendeskTicketFieldConstants.DfeEstablishmentNumberName => DfeEstablishmentNumberId,
                ZendeskTicketFieldConstants.UpnName => UpnId,
                ZendeskTicketFieldConstants.SchoolUrnName => SchoolUrnId,
                ZendeskTicketFieldConstants.LdsMatchedPupilIdName => LdsMatchedPupilIdId,
                ZendeskTicketFieldConstants.DateOfBirthCypmdName => DateOfBirthCypmdId,
                ZendeskTicketFieldConstants.AdmissionDateName => AdmissionDateId,
                ZendeskTicketFieldConstants.DecisionStatusName => DecisionStatusId,
                ZendeskTicketFieldConstants.CorrectionTypeName => CorrectionTypeId,
                ZendeskTicketFieldConstants.CycleYearName => CycleYearId,
                ZendeskTicketFieldConstants.CycleMonthName => CycleMonthId,
                ZendeskTicketFieldConstants.KeyStageName => KeyStageId,
                ZendeskTicketFieldConstants.SurnameCypmdName => SurnameCypmdId,
                ZendeskTicketFieldConstants.ForenameCypmdName => ForenameCypmdId,
                ZendeskTicketFieldConstants.SexName => SexId,
                ZendeskTicketFieldConstants.CorrectionReason31Name => CorrectionReason31Id,
                ZendeskTicketFieldConstants.ReasonForRemovalName => ReasonForRemovalId,
                ZendeskTicketFieldConstants.CypmdName => CypmdId,
                _ => null
            };
        }

        /// <summary>
        /// Gets all configured field IDs as a list of Key-Value pairs.
        /// </summary>
        public List<(string Name, long? Id)> GetAllFieldIds()
        {
            return new List<(string Name, long? Id)>
            {
                (ZendeskTicketFieldConstants.TypeOfOrganisationName, TypeOfOrganisationId),
                (ZendeskTicketFieldConstants.DfeEstablishmentNumberName, DfeEstablishmentNumberId),
                (ZendeskTicketFieldConstants.UpnName, UpnId),
                (ZendeskTicketFieldConstants.SchoolUrnName, SchoolUrnId),
                (ZendeskTicketFieldConstants.LdsMatchedPupilIdName, LdsMatchedPupilIdId),
                (ZendeskTicketFieldConstants.DateOfBirthCypmdName, DateOfBirthCypmdId),
                (ZendeskTicketFieldConstants.AdmissionDateName, AdmissionDateId),
                (ZendeskTicketFieldConstants.DecisionStatusName, DecisionStatusId),
                (ZendeskTicketFieldConstants.CorrectionTypeName, CorrectionTypeId),
                (ZendeskTicketFieldConstants.CycleYearName, CycleYearId),
                (ZendeskTicketFieldConstants.CycleMonthName, CycleMonthId),
                (ZendeskTicketFieldConstants.KeyStageName, KeyStageId),
                (ZendeskTicketFieldConstants.SurnameCypmdName, SurnameCypmdId),
                (ZendeskTicketFieldConstants.ForenameCypmdName, ForenameCypmdId),
                (ZendeskTicketFieldConstants.SexName, SexId),
                (ZendeskTicketFieldConstants.CorrectionReason31Name, CorrectionReason31Id),
                (ZendeskTicketFieldConstants.ReasonForRemovalName, ReasonForRemovalId),
                (ZendeskTicketFieldConstants.CypmdName, CypmdId)
            };
        }
    }
}
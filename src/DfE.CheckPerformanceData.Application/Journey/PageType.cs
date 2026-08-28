namespace DfE.CheckPerformanceData.Application.Journey;

// AB#296648 appends ResultSearch (pick one of a student's exam results) and ResultDetails
// (confirm the result and choose the revised grade). Appended last so existing flow JSON
// deserialises to unmoved values.
//
// AB#297848: QualificationSearch (pick the missing qualification by AO + QAN, resolved
// server-side like ResultSearch) and QualificationDetails (question page with the chosen
// qualification's summary card, like ResultDetails). Appended last.
public enum PageType
{
    Question, Content, EvidenceUpload, PupilSearch, ResultSearch, ResultDetails,
    QualificationSearch, QualificationDetails
}

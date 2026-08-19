namespace DfE.CheckPerformanceData.Application.Journey;

// AB#296648 appends ResultSearch (pick one of a student's exam results) and ResultDetails
// (confirm the result and choose the revised grade). Appended last so existing flow JSON
// deserialises to unmoved values.
public enum PageType { Question, Content, EvidenceUpload, PupilSearch, ResultSearch, ResultDetails }

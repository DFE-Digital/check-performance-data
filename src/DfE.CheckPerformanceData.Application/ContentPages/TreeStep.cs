namespace DfE.CheckPerformanceData.Application.ContentPages;

// One step of a path into a content tree. Index is the slot within the current column; Column
// selects which of a region's columns to descend into for that step. At the root the tree is a
// single implicit column, so the first step's Column is ignored (use 0).
public readonly record struct TreeStep(int Column, int Index);

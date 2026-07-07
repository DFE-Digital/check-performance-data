using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Domain.Enums;

public enum CheckingWindowType
{
    [Display(Name = "Key Stage 4 June")]
    KS4June,
    [Display(Name = "Key Stage 2")]
    KS2,
    [Display(Name = "Post 16")]
    Post16,
    [Display(Name = "Key Stage 4 Autumn")]
    KS4Autumn
}

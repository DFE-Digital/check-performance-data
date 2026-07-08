using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Domain.Enums;

public enum KeyStages
{
    [Display(Name = "Key stage 2")]
    KS2, //3-11
    [Display(Name = "Key stage 4")]
    KS4, //11-16
    [Display(Name = "Post 16")]
    Post16 //16-18
}

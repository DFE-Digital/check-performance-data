using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Seeding;

public sealed class DevDataSeeder(IPortalDbContext dbContext)
{
    private readonly Guid _keyStage4JuneCheckingWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private readonly Guid _closedKeyStage4JuneCheckingWindowId = Guid.Parse("44AEDD2C-7F3E-4F83-BB3D-47FBFAC1C604");

    public async Task SeedAsync()
    {
        await SeedCountries.ExecuteSeed(dbContext);

        await SeedCheckingWindows.ExecuteSeed(dbContext, _keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId);
        
        await dbContext.Pupils.ExecuteDeleteAsync();
        // Minehead Middle School
        await SeedPupils.ExecuteSeed(dbContext, urn: "136774", laestab: "933/4290",
            addIncluded: true,
            addNonIncluded: true,
            checkingWindowIds: [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);
        
        // Westfield Academy - Commented as they have no pupil data at all but left in to show they are not just forgotten.
        // await SeedPupils.ExecuteSeed(dbContext, urn: "137203", laestab: "933/4201",
        //     addIncluded: false,
        //     addNonIncluded: false,
        //     checkingWindowIds: [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);
        
        // EASTBROOK SCHOOL
        await SeedPupils.ExecuteSeed(dbContext, urn: "101243", laestab: "301/4023",
            addIncluded: false,
            addNonIncluded: true,
            checkingWindowIds: [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);
        
        // Alderwood School
        await SeedPupils.ExecuteSeed(dbContext, urn: "116234", laestab: "850/2729",
            addIncluded: true,
            addNonIncluded: false,
            checkingWindowIds: [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);
        
        // Kingsmead School
        await SeedPupils.ExecuteSeed(dbContext, urn: "142313", laestab: "860/4070",
            addIncluded: true,
            addNonIncluded: true,
            checkingWindowIds: [_keyStage4JuneCheckingWindowId, _closedKeyStage4JuneCheckingWindowId]);
    }
}

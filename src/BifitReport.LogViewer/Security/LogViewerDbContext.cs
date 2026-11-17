using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BifitReport.LogViewer.Security;

public sealed class LogViewerDbContext(DbContextOptions<LogViewerDbContext> options)
    : IdentityDbContext<LogViewerUser>(options)
{
}

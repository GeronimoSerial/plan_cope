using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;

namespace PlanCope.Shared.Infrastructure.DependencyInjection;

public static class SharedInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPlanCopeSharedInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<Exam>, ExamValidator>();
        services.AddSingleton<IValidator<ExamVersion>, ExamVersionValidator>();
        services.AddSingleton<IValidator<ExamBlock>, ExamBlockValidator>();
        services.AddSingleton<IValidator<CreateSessionRequest>, CreateSessionRequestValidator>();
        services.AddSingleton<IValidator<PushRequest>, PushRequestValidator>();

        return services;
    }
}

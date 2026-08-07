using System;
using System.Collections.Generic;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public sealed record EmployeeRatingRuleDefinition(
        string Code,
        int Version,
        EmployeeRatingBranch Branch,
        EmployeeRatingEffectDirection Direction,
        int ChangePercent,
        TimeSpan Duration,
        string Title);

    public static class EmployeeRatingRuleCatalog
    {
        private static readonly IReadOnlyDictionary<string, EmployeeRatingRuleDefinition> Rules =
            new Dictionary<string, EmployeeRatingRuleDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["TIME_FIRST_OPEN_PUNCTUAL"] = new(
                    "TIME_FIRST_OPEN_PUNCTUAL", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Reward, 5, TimeSpan.FromHours(24),
                    "Пунктуальное открытие клуба"),
                ["TIME_FIRST_OPEN_SLIGHTLY_LATE"] = new(
                    "TIME_FIRST_OPEN_SLIGHTLY_LATE", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Penalty, 3, TimeSpan.FromHours(12),
                    "Небольшое опоздание при открытии"),
                ["TIME_FIRST_OPEN_LATE"] = new(
                    "TIME_FIRST_OPEN_LATE", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Penalty, 5, TimeSpan.FromHours(24),
                    "Позднее открытие клуба"),
                ["TIME_PC_LEFT_UNATTENDED"] = new(
                    "TIME_PC_LEFT_UNATTENDED", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Penalty, 7, TimeSpan.FromDays(4),
                    "ПК оставлен без клиентов"),
                ["TIME_EXPIRED_TV_UNATTENDED"] = new(
                    "TIME_EXPIRED_TV_UNATTENDED", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Penalty, 5, TimeSpan.FromDays(2),
                    "ТВ оставлен после окончания тарифа"),
                ["TIME_LATE_CLIENT_REWARD"] = new(
                    "TIME_LATE_CLIENT_REWARD", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Reward, 3, TimeSpan.FromDays(2),
                    "Работа с клиентом после 01:00"),
                ["REVENUE_CONFIRMED_LOSS_SMALL"] = new(
                    "REVENUE_CONFIRMED_LOSS_SMALL", 2, EmployeeRatingBranch.Revenue,
                    EmployeeRatingEffectDirection.Penalty, 3, TimeSpan.FromDays(4),
                    "Подтверждённая потеря до 100 сом"),
                ["REVENUE_CONFIRMED_LOSS_LARGE"] = new(
                    "REVENUE_CONFIRMED_LOSS_LARGE", 2, EmployeeRatingBranch.Revenue,
                    EmployeeRatingEffectDirection.Penalty, 5, TimeSpan.FromDays(4),
                    "Подтверждённая потеря свыше 100 сом"),
                ["REVENUE_CONFIRMED_EXTRA"] = new(
                    "REVENUE_CONFIRMED_EXTRA", 2, EmployeeRatingBranch.Revenue,
                    EmployeeRatingEffectDirection.Reward, 5, TimeSpan.FromDays(2),
                    "Подтверждённый излишек"),
                ["TIME_OTHER_VIOLATION"] = new(
                    "TIME_OTHER_VIOLATION", 2, EmployeeRatingBranch.Time,
                    EmployeeRatingEffectDirection.Penalty, 5, TimeSpan.FromDays(2),
                    "Подтверждённое нарушение правил")
            };

        public static EmployeeRatingRuleDefinition Get(string code)
        {
            if (!Rules.TryGetValue(code.Trim(), out var rule))
                throw new InvalidOperationException($"Правило рейтинга не найдено: {code}.");

            return rule;
        }
    }
}

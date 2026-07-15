using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClubTimerXbox.Models;

namespace ClubTimerXbox.Services
{
    public static class FirebaseChannelBindingService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        private static readonly SemaphoreSlim CheckLock = new SemaphoreSlim(1, 1);
        private static readonly object StatusLock = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static FirebaseChannelBindingStatus _status =
            FirebaseChannelBindingStatus.Create(
                FirebaseChannelBindingState.Unknown,
                "Связь с каналом еще не проверена."
            );

        private static DateTime _nextCheckUtc = DateTime.MinValue;

        public static FirebaseChannelBindingStatus CurrentStatus
        {
            get
            {
                lock (StatusLock)
                    return _status.Clone();
            }
        }

        public static bool IsCurrentBindingConfirmed
        {
            get
            {
                PcIdentity identity = PcIdentityService.Current;

                lock (StatusLock)
                {
                    return _status.State == FirebaseChannelBindingState.Bound &&
                           _status.ClubId.Equals(
                               identity.ClubId,
                               StringComparison.OrdinalIgnoreCase
                           ) &&
                           _status.InstallationId.Equals(
                               identity.InstallationId,
                               StringComparison.OrdinalIgnoreCase
                           );
                }
            }
        }

        public static async Task<bool> EnsureCurrentBindingAsync(bool force = false)
        {
            if (!PcIdentityService.HasAssignedClub)
            {
                SetStatus(
                    FirebaseChannelBindingStatus.Create(
                        FirebaseChannelBindingState.Unassigned,
                        "Канал телефона не выбран. Локальная касса продолжает работать."
                    ),
                    TimeSpan.FromSeconds(15)
                );
                return false;
            }

            if (!FirebaseAuthService.HasSavedSession)
            {
                PcIdentity identity = PcIdentityService.Current;
                SetStatus(
                    FirebaseChannelBindingStatus.ForIdentity(
                        FirebaseChannelBindingState.AuthenticationRequired,
                        identity,
                        "Нет сохраненного входа Firebase. Локальная касса продолжает работать."
                    ),
                    TimeSpan.FromSeconds(15)
                );
                return false;
            }

            if (!force && DateTime.UtcNow < _nextCheckUtc)
                return IsCurrentBindingConfirmed;

            if (force)
            {
                await CheckLock.WaitAsync().ConfigureAwait(false);
            }
            else if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
            {
                return IsCurrentBindingConfirmed;
            }

            try
            {
                if (!force && DateTime.UtcNow < _nextCheckUtc)
                    return IsCurrentBindingConfirmed;

                return await EnsureCurrentBindingCoreAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == HttpStatusCode.Unauthorized ||
                ex.StatusCode == HttpStatusCode.Forbidden)
            {
                SetAuthenticationFailure(PcIdentityService.Current);
                return false;
            }
            catch (TaskCanceledException)
            {
                SetTemporaryFailure(
                    PcIdentityService.Current,
                    "Firebase пока не отвечает. Локальная касса продолжает работать."
                );
                return false;
            }
            catch
            {
                SetTemporaryFailure(
                    PcIdentityService.Current,
                    "Связь с Firebase временно недоступна. Локальная касса продолжает работать."
                );
                return false;
            }
            finally
            {
                CheckLock.Release();
            }
        }

        public static async Task<FirebaseChannelCatalogResult> GetChannelCatalogAsync()
        {
            if (!FirebaseAuthService.HasSavedSession)
            {
                return FirebaseChannelCatalogResult.Fail(
                    "Сначала выполните вход в Firebase."
                );
            }

            try
            {
                PcIdentity identity = PcIdentityService.Current;

                Task<Dictionary<string, FirebaseOwnerClub>?> ownerTask =
                    GetJsonAsync<Dictionary<string, FirebaseOwnerClub>>("owner/clubs");
                Task<Dictionary<string, FirebaseChannelBinding>?> bindingTask =
                    GetJsonAsync<Dictionary<string, FirebaseChannelBinding>>("channelBindings");
                Task<FirebaseInstallationBinding?> installationTask =
                    GetJsonAsync<FirebaseInstallationBinding>(
                        InstallationBindingPath(identity.InstallationId)
                    );

                await Task.WhenAll(ownerTask, bindingTask, installationTask)
                    .ConfigureAwait(false);

                Dictionary<string, FirebaseOwnerClub> ownerClubs =
                    ToCaseInsensitiveDictionary(await ownerTask.ConfigureAwait(false));
                Dictionary<string, FirebaseChannelBinding> bindings =
                    ToCaseInsensitiveDictionary(await bindingTask.ConfigureAwait(false));
                FirebaseInstallationBinding? installationBinding =
                    await installationTask.ConfigureAwait(false);

                var clubIds = new HashSet<string>(
                    ownerClubs.Keys,
                    StringComparer.OrdinalIgnoreCase
                );
                clubIds.UnionWith(bindings.Keys);

                if (!string.IsNullOrWhiteSpace(identity.ClubId))
                    clubIds.Add(identity.ClubId);

                if (!string.IsNullOrWhiteSpace(installationBinding?.ClubId))
                    clubIds.Add(installationBinding.ClubId);

                var channels = new List<FirebaseChannelOption>();

                foreach (string clubId in clubIds)
                {
                    ownerClubs.TryGetValue(clubId, out FirebaseOwnerClub? ownerClub);
                    bindings.TryGetValue(clubId, out FirebaseChannelBinding? binding);

                    bool isLocalChannel = clubId.Equals(
                        identity.ClubId,
                        StringComparison.OrdinalIgnoreCase
                    );
                    bool reversePointsHere =
                        installationBinding != null &&
                        installationBinding.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        ) &&
                        installationBinding.ClubId.Equals(
                            clubId,
                            StringComparison.OrdinalIgnoreCase
                        );
                    bool reversePointsElsewhere =
                        installationBinding != null &&
                        installationBinding.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        ) &&
                        !string.IsNullOrWhiteSpace(installationBinding.ClubId) &&
                        !reversePointsHere;

                    bool bindingOwnedByThisPc =
                        binding != null &&
                        binding.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        );
                    bool bindingOwnedByAnotherPc =
                        binding != null &&
                        !string.IsNullOrWhiteSpace(binding.InstallationId) &&
                        !bindingOwnedByThisPc;

                    bool legacyOwnedByThisPc =
                        binding == null &&
                        ownerClub != null &&
                        !string.IsNullOrWhiteSpace(ownerClub.InstallationId) &&
                        ownerClub.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        );
                    bool legacyOwnedByAnotherPc =
                        binding == null &&
                        ownerClub != null &&
                        ownerClub.IsActivated &&
                        !string.IsNullOrWhiteSpace(ownerClub.InstallationId) &&
                        !legacyOwnedByThisPc;

                    if (ownerClub?.IsDeleted == true &&
                        !isLocalChannel &&
                        !reversePointsHere &&
                        binding == null)
                    {
                        continue;
                    }

                    FirebaseChannelAvailability availability;
                    string occupiedPcName = "";

                    if (bindingOwnedByAnotherPc)
                    {
                        availability = FirebaseChannelAvailability.Occupied;
                        occupiedPcName = DisplayPcName(binding?.PcName);
                    }
                    else if (legacyOwnedByAnotherPc)
                    {
                        availability = FirebaseChannelAvailability.Occupied;
                        occupiedPcName = DisplayPcName(ownerClub?.PcName);
                    }
                    else if ((bindingOwnedByThisPc || legacyOwnedByThisPc) &&
                             reversePointsElsewhere)
                    {
                        availability = FirebaseChannelAvailability.Occupied;
                        occupiedPcName = "прежняя запись этого ПК";
                    }
                    else if (reversePointsHere ||
                             (isLocalChannel && !reversePointsElsewhere))
                    {
                        availability = FirebaseChannelAvailability.Current;
                    }
                    else if (bindingOwnedByThisPc || legacyOwnedByThisPc)
                    {
                        availability = FirebaseChannelAvailability.Occupied;
                        occupiedPcName = "прежняя запись этого ПК";
                    }
                    else
                    {
                        availability = FirebaseChannelAvailability.Available;
                    }

                    string clubName = FirstNotEmpty(
                        ownerClub?.Name,
                        binding?.ClubName,
                        isLocalChannel ? identity.ClubName : "",
                        clubId
                    );

                    channels.Add(new FirebaseChannelOption
                    {
                        ClubId = clubId,
                        ClubName = clubName,
                        Availability = availability,
                        OccupiedPcName = occupiedPcName
                    });
                }

                List<FirebaseChannelOption> ordered = channels
                    .OrderBy(channel => AvailabilityOrder(channel.Availability))
                    .ThenBy(channel => ClubNumber(channel.ClubId))
                    .ThenBy(channel => channel.ClubName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(channel => channel.ClubId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int availableCount = ordered.Count(channel =>
                    channel.Availability == FirebaseChannelAvailability.Available
                );

                return FirebaseChannelCatalogResult.Ok(
                    ordered,
                    $"Каналов: {ordered.Count}. Свободных для привязки: {availableCount}."
                );
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == HttpStatusCode.Unauthorized ||
                ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return FirebaseChannelCatalogResult.Fail(
                    "Firebase отклонил сохраненный вход. Выполните вход заново."
                );
            }
            catch (TaskCanceledException)
            {
                return FirebaseChannelCatalogResult.Fail(
                    "Firebase не ответил вовремя. Повторите обновление списка."
                );
            }
            catch
            {
                return FirebaseChannelCatalogResult.Fail(
                    "Не удалось получить список каналов."
                );
            }
        }

        public static async Task<FirebaseChannelSwitchResult> TrySwitchCurrentChannelAsync(
            string requestedClubId)
        {
            if (!PcIdentityService.TryNormalizeClubId(requestedClubId, out string clubId))
            {
                return FirebaseChannelSwitchResult.Fail(
                    "Выберите канал из списка."
                );
            }

            if (!FirebaseAuthService.HasSavedSession)
            {
                return FirebaseChannelSwitchResult.Fail(
                    "Сначала выполните вход в Firebase."
                );
            }

            PcIdentity previousIdentity = PcIdentityService.Current;

            if (previousIdentity.ClubId.Equals(
                    clubId,
                    StringComparison.OrdinalIgnoreCase))
            {
                bool currentBound = await EnsureCurrentBindingAsync(force: true)
                    .ConfigureAwait(false);

                return currentBound
                    ? FirebaseChannelSwitchResult.Ok(
                        previousIdentity.ClubId,
                        previousIdentity.ClubName,
                        "Этот канал уже выбран и надежно привязан к ПК."
                    )
                    : FirebaseChannelSwitchResult.Fail(CurrentStatus.Message);
            }

            await CheckLock.WaitAsync().ConfigureAwait(false);

            try
            {
                FirebaseOwnerClub? ownerClub = await GetJsonAsync<FirebaseOwnerClub>(
                    $"owner/clubs/{clubId}"
                ).ConfigureAwait(false);

                if (ownerClub == null || ownerClub.IsDeleted)
                {
                    return FirebaseChannelSwitchResult.Fail(
                        "Канал больше не существует. Обновите список."
                    );
                }

                string clubName = FirstNotEmpty(ownerClub.Name, clubId);
                PcIdentity candidate = PcIdentityService.Current;
                candidate.ClubId = clubId;
                candidate.ClubName = clubName;
                candidate.IsActivated = true;
                candidate.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    FirebaseConditionalValue<FirebaseChannelBinding> targetSnapshot =
                        await ReadConditionalAsync<FirebaseChannelBinding>(
                            ChannelBindingPath(clubId)
                        ).ConfigureAwait(false);

                    FirebaseChannelBinding? existingTarget = targetSnapshot.Value;

                    if (existingTarget != null &&
                        !existingTarget.InstallationId.Equals(
                            candidate.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        return FirebaseChannelSwitchResult.Fail(
                            $"Канал {clubId} уже занят ПК {DisplayPcName(existingTarget.PcName)}."
                        );
                    }

                    if (existingTarget == null)
                    {
                        string legacyInstallationId = ownerClub.InstallationId?.Trim() ?? "";
                        string legacyPcName = ownerClub.PcName?.Trim() ?? "";

                        if (string.IsNullOrWhiteSpace(legacyInstallationId))
                        {
                            FirebaseClubMeta? legacyMeta =
                                await ReadLegacyClubMetaAsync(clubId).ConfigureAwait(false);
                            legacyInstallationId = legacyMeta?.InstallationId?.Trim() ?? "";
                            legacyPcName = FirstNotEmpty(legacyPcName, legacyMeta?.PcName);
                        }

                        if (!string.IsNullOrWhiteSpace(legacyInstallationId) &&
                            !legacyInstallationId.Equals(
                                candidate.InstallationId,
                                StringComparison.OrdinalIgnoreCase
                            ))
                        {
                            return FirebaseChannelSwitchResult.Fail(
                                $"Канал {clubId} уже используется ПК {DisplayPcName(legacyPcName)}."
                            );
                        }
                    }

                    FirebaseConditionalValue<FirebaseInstallationBinding> reverseSnapshot =
                        await ReadConditionalAsync<FirebaseInstallationBinding>(
                            InstallationBindingPath(candidate.InstallationId)
                        ).ConfigureAwait(false);

                    FirebaseInstallationBinding? existingReverse = reverseSnapshot.Value;

                    if (existingReverse != null &&
                        !string.IsNullOrWhiteSpace(existingReverse.ClubId) &&
                        !existingReverse.ClubId.Equals(
                            clubId,
                            StringComparison.OrdinalIgnoreCase
                        ) &&
                        !existingReverse.ClubId.Equals(
                            previousIdentity.ClubId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        FirebaseConditionalValue<FirebaseChannelBinding> authoritativeChannel =
                            await ReadConditionalAsync<FirebaseChannelBinding>(
                                ChannelBindingPath(existingReverse.ClubId)
                            ).ConfigureAwait(false);

                        if (authoritativeChannel.Value != null &&
                            authoritativeChannel.Value.InstallationId.Equals(
                                candidate.InstallationId,
                                StringComparison.OrdinalIgnoreCase
                            ))
                        {
                            return FirebaseChannelSwitchResult.Fail(
                                $"Этот ПК уже закреплен за каналом {existingReverse.ClubId}. " +
                                "Сначала выберите его в списке."
                            );
                        }
                    }

                    FirebaseChannelBinding targetBinding =
                        BuildChannelBinding(candidate, existingTarget);

                    bool targetClaimed = await TryPutConditionalAsync(
                        ChannelBindingPath(clubId),
                        targetBinding,
                        targetSnapshot.ETag
                    ).ConfigureAwait(false);

                    if (!targetClaimed)
                        continue;

                    string previousClubId =
                        !string.IsNullOrWhiteSpace(previousIdentity.ClubId) &&
                        !previousIdentity.ClubId.Equals(
                            clubId,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? previousIdentity.ClubId
                            : existingReverse?.PreviousClubId?.Trim() ?? "";

                    FirebaseInstallationBinding reverseBinding =
                        BuildInstallationBinding(
                            candidate,
                            existingReverse,
                            previousClubId
                        );

                    bool reverseClaimed = await TryPutConditionalAsync(
                        InstallationBindingPath(candidate.InstallationId),
                        reverseBinding,
                        reverseSnapshot.ETag
                    ).ConfigureAwait(false);

                    if (!reverseClaimed)
                    {
                        if (existingTarget == null)
                        {
                            await TryRollbackNewChannelClaimAsync(
                                clubId,
                                candidate.InstallationId
                            ).ConfigureAwait(false);
                        }

                        continue;
                    }

                    bool targetStillOwned = await EnsureTargetStillOwnedAsync(
                        candidate,
                        targetBinding
                    ).ConfigureAwait(false);

                    if (!targetStillOwned)
                    {
                        await TryRestoreInstallationBindingAsync(
                            candidate.InstallationId,
                            clubId,
                            existingReverse
                        ).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        PcIdentityService.Save(candidate);
                    }
                    catch (Exception ex)
                    {
                        await TryRestoreInstallationBindingAsync(
                            candidate.InstallationId,
                            clubId,
                            existingReverse
                        ).ConfigureAwait(false);

                        if (existingTarget == null)
                        {
                            await TryRollbackNewChannelClaimAsync(
                                clubId,
                                candidate.InstallationId
                            ).ConfigureAwait(false);
                        }

                        ResetRuntimeStatus();
                        return FirebaseChannelSwitchResult.Fail(
                            "Не удалось сохранить канал на ПК: " + ex.Message
                        );
                    }

                    bool targetMetadataUpdated =
                        await TryMarkClubAssignedAsync(candidate).ConfigureAwait(false);
                    bool oldChannelReleased =
                        await TryCompletePendingReleaseAsync(candidate).ConfigureAwait(false);

                    string message =
                        targetMetadataUpdated && oldChannelReleased
                            ? $"Канал {clubName} ({clubId}) привязан. Старый канал освобожден."
                            : $"Канал {clubName} ({clubId}) привязан. " +
                              "Очистка старой записи завершится автоматически.";

                    SetStatus(
                        FirebaseChannelBindingStatus.ForIdentity(
                            FirebaseChannelBindingState.Bound,
                            candidate,
                            message
                        ),
                        TimeSpan.FromSeconds(15)
                    );

                    return FirebaseChannelSwitchResult.Ok(
                        clubId,
                        clubName,
                        message
                    );
                }

                return FirebaseChannelSwitchResult.Fail(
                    "Список каналов изменился во время привязки. Обновите его и повторите."
                );
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == HttpStatusCode.Unauthorized ||
                ex.StatusCode == HttpStatusCode.Forbidden)
            {
                SetAuthenticationFailure(PcIdentityService.Current);
                return FirebaseChannelSwitchResult.Fail(
                    "Firebase отклонил сохраненный вход. Выполните вход заново."
                );
            }
            catch (TaskCanceledException)
            {
                SetTemporaryFailure(
                    PcIdentityService.Current,
                    "Firebase не ответил вовремя. Локальная касса продолжает работать."
                );
                return FirebaseChannelSwitchResult.Fail(CurrentStatus.Message);
            }
            catch (Exception ex)
            {
                SetTemporaryFailure(
                    PcIdentityService.Current,
                    "Не удалось переключить канал. Локальная касса продолжает работать."
                );
                return FirebaseChannelSwitchResult.Fail(
                    "Не удалось переключить канал: " + ex.Message
                );
            }
            finally
            {
                CheckLock.Release();
            }
        }

        public static void ResetRuntimeStatus()
        {
            lock (StatusLock)
            {
                _status = FirebaseChannelBindingStatus.Create(
                    FirebaseChannelBindingState.Unknown,
                    "Связь с каналом еще не проверена."
                );
                _nextCheckUtc = DateTime.MinValue;
            }
        }

        private static async Task<bool> EnsureCurrentBindingCoreAsync()
        {
            PcIdentity identity = PcIdentityService.Current;

            SetStatus(
                FirebaseChannelBindingStatus.ForIdentity(
                    FirebaseChannelBindingState.Checking,
                    identity,
                    "Проверяем привязку канала..."
                ),
                TimeSpan.Zero
            );

            for (int attempt = 0; attempt < 4; attempt++)
            {
                FirebaseConditionalValue<FirebaseChannelBinding> channelSnapshot =
                    await ReadConditionalAsync<FirebaseChannelBinding>(
                        ChannelBindingPath(identity.ClubId)
                    ).ConfigureAwait(false);

                FirebaseChannelBinding? existing = channelSnapshot.Value;

                if (existing != null &&
                    !existing.InstallationId.Equals(
                        identity.InstallationId,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    string occupiedBy = DisplayPcName(existing.PcName);
                    SetStatus(
                        FirebaseChannelBindingStatus.ForConflict(
                            identity,
                            existing.InstallationId,
                            occupiedBy,
                            $"Канал {identity.ClubId} уже занят ПК {occupiedBy}. " +
                            "Синхронизация этого экземпляра остановлена."
                        ),
                        TimeSpan.FromSeconds(15)
                    );
                    return false;
                }

                if (existing != null &&
                    !string.IsNullOrWhiteSpace(existing.AuthUid) &&
                    !existing.AuthUid.Equals(
                        FirebaseAuthService.CurrentUserId,
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !FirebaseAuthService.CanManageAllClubs)
                {
                    SetStatus(
                        FirebaseChannelBindingStatus.ForIdentity(
                            FirebaseChannelBindingState.AuthenticationRequired,
                            identity,
                            "Канал привязан к другому Firebase аккаунту. " +
                            "Войдите под аккаунтом этого клуба."
                        ),
                        TimeSpan.FromSeconds(30)
                    );
                    return false;
                }

                if (existing == null)
                {
                    FirebaseClubMeta? legacyMeta =
                        await ReadLegacyClubMetaAsync(identity.ClubId).ConfigureAwait(false);

                    if (legacyMeta != null &&
                        !string.IsNullOrWhiteSpace(legacyMeta.InstallationId) &&
                        !legacyMeta.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        string occupiedBy = DisplayPcName(legacyMeta.PcName);
                        SetStatus(
                            FirebaseChannelBindingStatus.ForConflict(
                                identity,
                                legacyMeta.InstallationId,
                                occupiedBy,
                                $"Канал {identity.ClubId} уже используется ПК {occupiedBy}. " +
                                "Автоматический захват запрещен."
                            ),
                            TimeSpan.FromSeconds(15)
                        );
                        return false;
                    }
                }

                FirebaseChannelBinding candidate =
                    BuildChannelBinding(identity, existing);

                if (existing == null || ChannelMetadataChanged(existing, candidate))
                {
                    bool channelClaimed = await TryPutConditionalAsync(
                        ChannelBindingPath(identity.ClubId),
                        candidate,
                        channelSnapshot.ETag
                    ).ConfigureAwait(false);

                    if (!channelClaimed)
                        continue;
                }

                InstallationBindingCheck installationCheck =
                    await EnsureInstallationBindingAsync(identity).ConfigureAwait(false);

                if (!installationCheck.Success)
                {
                    if (existing == null)
                    {
                        await TryRollbackNewChannelClaimAsync(
                            identity.ClubId,
                            identity.InstallationId
                        ).ConfigureAwait(false);
                    }

                    return false;
                }

                bool oldChannelReleased =
                    await TryCompletePendingReleaseAsync(identity).ConfigureAwait(false);

                string message = oldChannelReleased
                    ? "Канал надежно привязан к этому ПК."
                    : "Канал привязан. Освобождение прежнего канала завершится автоматически.";

                SetStatus(
                    FirebaseChannelBindingStatus.ForIdentity(
                        FirebaseChannelBindingState.Bound,
                        identity,
                        message
                    ),
                    TimeSpan.FromSeconds(15)
                );
                return true;
            }

            SetTemporaryFailure(
                identity,
                "Канал изменился во время проверки. Повторим автоматически."
            );
            return false;
        }

        private static async Task<InstallationBindingCheck> EnsureInstallationBindingAsync(
            PcIdentity identity)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                FirebaseConditionalValue<FirebaseInstallationBinding> snapshot =
                    await ReadConditionalAsync<FirebaseInstallationBinding>(
                        InstallationBindingPath(identity.InstallationId)
                    ).ConfigureAwait(false);

                FirebaseInstallationBinding? existing = snapshot.Value;

                if (existing != null &&
                    !string.IsNullOrWhiteSpace(existing.ClubId) &&
                    !existing.ClubId.Equals(
                        identity.ClubId,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    FirebaseConditionalValue<FirebaseChannelBinding> oldChannel =
                        await ReadConditionalAsync<FirebaseChannelBinding>(
                            ChannelBindingPath(existing.ClubId)
                        ).ConfigureAwait(false);

                    if (oldChannel.Value != null &&
                        oldChannel.Value.InstallationId.Equals(
                            identity.InstallationId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        SetStatus(
                            FirebaseChannelBindingStatus.ForConflict(
                                identity,
                                identity.InstallationId,
                                Environment.MachineName,
                                $"Этот ПК уже закреплен за каналом {existing.ClubId}. " +
                                "Синхронизация второго канала запрещена."
                            ),
                            TimeSpan.FromSeconds(15)
                        );
                        return InstallationBindingCheck.Fail();
                    }
                }

                FirebaseInstallationBinding candidate =
                    BuildInstallationBinding(
                        identity,
                        existing,
                        existing != null &&
                        existing.ClubId.Equals(
                            identity.ClubId,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? existing.PreviousClubId
                            : ""
                    );

                if (existing != null &&
                    existing.ClubId.Equals(
                        identity.ClubId,
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !InstallationMetadataChanged(existing, candidate))
                {
                    return InstallationBindingCheck.Ok(existing);
                }

                bool claimed = await TryPutConditionalAsync(
                    InstallationBindingPath(identity.InstallationId),
                    candidate,
                    snapshot.ETag
                ).ConfigureAwait(false);

                if (claimed)
                    return InstallationBindingCheck.Ok(candidate);
            }

            SetTemporaryFailure(
                identity,
                "Привязка установки изменилась во время проверки. Повторим автоматически."
            );
            return InstallationBindingCheck.Fail();
        }

        private static async Task<bool> EnsureTargetStillOwnedAsync(
            PcIdentity identity,
            FirebaseChannelBinding expectedBinding)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                FirebaseConditionalValue<FirebaseChannelBinding> snapshot =
                    await ReadConditionalAsync<FirebaseChannelBinding>(
                        ChannelBindingPath(identity.ClubId)
                    ).ConfigureAwait(false);

                if (snapshot.Value != null)
                {
                    return snapshot.Value.InstallationId.Equals(
                        identity.InstallationId,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                bool restored = await TryPutConditionalAsync(
                    ChannelBindingPath(identity.ClubId),
                    expectedBinding,
                    snapshot.ETag
                ).ConfigureAwait(false);

                if (restored)
                    return true;
            }

            return false;
        }

        private static async Task<bool> TryCompletePendingReleaseAsync(PcIdentity identity)
        {
            try
            {
                FirebaseConditionalValue<FirebaseInstallationBinding> reverseSnapshot =
                    await ReadConditionalAsync<FirebaseInstallationBinding>(
                        InstallationBindingPath(identity.InstallationId)
                    ).ConfigureAwait(false);

                FirebaseInstallationBinding? reverse = reverseSnapshot.Value;

                if (reverse == null ||
                    !reverse.InstallationId.Equals(
                        identity.InstallationId,
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    !reverse.ClubId.Equals(
                        identity.ClubId,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return false;
                }

                string previousClubId = reverse.PreviousClubId?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(previousClubId) ||
                    previousClubId.Equals(
                        identity.ClubId,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return true;
                }

                ChannelReleaseResult releaseResult =
                    await ReleaseChannelBindingIfOwnedAsync(
                        previousClubId,
                        identity.InstallationId
                    ).ConfigureAwait(false);

                if (releaseResult == ChannelReleaseResult.RetryRequired)
                    return false;

                if (releaseResult != ChannelReleaseResult.OwnedByAnotherInstallation)
                {
                    FirebaseConditionalValue<FirebaseChannelBinding> oldChannel =
                        await ReadConditionalAsync<FirebaseChannelBinding>(
                            ChannelBindingPath(previousClubId)
                        ).ConfigureAwait(false);

                    if (oldChannel.Value == null)
                    {
                        bool oldMetadataCleared =
                            await TryMarkClubUnassignedAsync(previousClubId)
                                .ConfigureAwait(false);

                        if (!oldMetadataCleared)
                            return false;
                    }
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    FirebaseConditionalValue<FirebaseInstallationBinding> latestSnapshot =
                        await ReadConditionalAsync<FirebaseInstallationBinding>(
                            InstallationBindingPath(identity.InstallationId)
                        ).ConfigureAwait(false);

                    FirebaseInstallationBinding? latest = latestSnapshot.Value;

                    if (latest == null ||
                        !latest.ClubId.Equals(
                            identity.ClubId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(latest.PreviousClubId))
                        return true;

                    latest.PreviousClubId = "";
                    latest.UpdatedAt = DateTime.UtcNow.ToString("O");
                    latest.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    bool cleared = await TryPutConditionalAsync(
                        InstallationBindingPath(identity.InstallationId),
                        latest,
                        latestSnapshot.ETag
                    ).ConfigureAwait(false);

                    if (cleared)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<ChannelReleaseResult> ReleaseChannelBindingIfOwnedAsync(
            string clubId,
            string installationId)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                FirebaseConditionalValue<FirebaseChannelBinding> snapshot =
                    await ReadConditionalAsync<FirebaseChannelBinding>(
                        ChannelBindingPath(clubId)
                    ).ConfigureAwait(false);

                if (snapshot.Value == null)
                    return ChannelReleaseResult.AlreadyFree;

                if (!snapshot.Value.InstallationId.Equals(
                        installationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ChannelReleaseResult.OwnedByAnotherInstallation;
                }

                bool deleted = await TryDeleteConditionalAsync(
                    ChannelBindingPath(clubId),
                    snapshot.ETag
                ).ConfigureAwait(false);

                if (deleted)
                    return ChannelReleaseResult.Released;
            }

            return ChannelReleaseResult.RetryRequired;
        }

        private static async Task TryRollbackNewChannelClaimAsync(
            string clubId,
            string installationId)
        {
            try
            {
                FirebaseInstallationBinding? reverse =
                    await GetJsonAsync<FirebaseInstallationBinding>(
                        InstallationBindingPath(installationId)
                    ).ConfigureAwait(false);

                if (reverse != null &&
                    reverse.ClubId.Equals(clubId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await ReleaseChannelBindingIfOwnedAsync(clubId, installationId)
                    .ConfigureAwait(false);
            }
            catch
            {
                // A non-authoritative claim is harmless because sync also checks reverse ownership.
            }
        }

        private static async Task TryRestoreInstallationBindingAsync(
            string installationId,
            string expectedClubId,
            FirebaseInstallationBinding? previousBinding)
        {
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    FirebaseConditionalValue<FirebaseInstallationBinding> snapshot =
                        await ReadConditionalAsync<FirebaseInstallationBinding>(
                            InstallationBindingPath(installationId)
                        ).ConfigureAwait(false);

                    if (snapshot.Value == null ||
                        !snapshot.Value.ClubId.Equals(
                            expectedClubId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    {
                        return;
                    }

                    bool restored = previousBinding == null
                        ? await TryDeleteConditionalAsync(
                            InstallationBindingPath(installationId),
                            snapshot.ETag
                        ).ConfigureAwait(false)
                        : await TryPutConditionalAsync(
                            InstallationBindingPath(installationId),
                            previousBinding,
                            snapshot.ETag
                        ).ConfigureAwait(false);

                    if (restored)
                        return;
                }
            }
            catch
            {
                // The next binding check will resolve any incomplete rollback.
            }
        }

        private static FirebaseChannelBinding BuildChannelBinding(
            PcIdentity identity,
            FirebaseChannelBinding? existing)
        {
            string nowUtc = DateTime.UtcNow.ToString("O");
            string nowLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            return new FirebaseChannelBinding
            {
                ClubId = identity.ClubId,
                ClubName = identity.ClubName,
                InstallationId = identity.InstallationId,
                AuthUid = FirstNotEmpty(existing?.AuthUid, FirebaseAuthService.CurrentUserId),
                PcName = Environment.MachineName,
                AppVersion = AppVersionService.Version,
                ClaimedAt = FirstNotEmpty(existing?.ClaimedAt, nowUtc),
                ClaimedAtLocal = FirstNotEmpty(existing?.ClaimedAtLocal, nowLocal),
                UpdatedAt = nowUtc,
                UpdatedAtLocal = nowLocal
            };
        }

        private static FirebaseInstallationBinding BuildInstallationBinding(
            PcIdentity identity,
            FirebaseInstallationBinding? existing,
            string previousClubId)
        {
            string nowUtc = DateTime.UtcNow.ToString("O");
            string nowLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            bool sameClub =
                existing != null &&
                existing.ClubId.Equals(
                    identity.ClubId,
                    StringComparison.OrdinalIgnoreCase
                );

            return new FirebaseInstallationBinding
            {
                InstallationId = identity.InstallationId,
                ClubId = identity.ClubId,
                ClubName = identity.ClubName,
                AuthUid = FirstNotEmpty(existing?.AuthUid, FirebaseAuthService.CurrentUserId),
                PcName = Environment.MachineName,
                AppVersion = AppVersionService.Version,
                BoundAt = sameClub
                    ? FirstNotEmpty(existing?.BoundAt, nowUtc)
                    : nowUtc,
                BoundAtLocal = sameClub
                    ? FirstNotEmpty(existing?.BoundAtLocal, nowLocal)
                    : nowLocal,
                PreviousClubId = previousClubId?.Trim() ?? "",
                UpdatedAt = nowUtc,
                UpdatedAtLocal = nowLocal
            };
        }

        private static bool ChannelMetadataChanged(
            FirebaseChannelBinding existing,
            FirebaseChannelBinding candidate)
        {
            return !existing.ClubId.Equals(candidate.ClubId, StringComparison.OrdinalIgnoreCase) ||
                   !existing.ClubName.Equals(candidate.ClubName, StringComparison.Ordinal) ||
                   !existing.InstallationId.Equals(
                       candidate.InstallationId,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.AuthUid.Equals(
                       candidate.AuthUid,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.PcName.Equals(
                       candidate.PcName,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.AppVersion.Equals(candidate.AppVersion, StringComparison.Ordinal);
        }

        private static bool InstallationMetadataChanged(
            FirebaseInstallationBinding existing,
            FirebaseInstallationBinding candidate)
        {
            return !existing.InstallationId.Equals(
                       candidate.InstallationId,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.ClubId.Equals(candidate.ClubId, StringComparison.OrdinalIgnoreCase) ||
                   !existing.ClubName.Equals(candidate.ClubName, StringComparison.Ordinal) ||
                   !existing.AuthUid.Equals(
                       candidate.AuthUid,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.PcName.Equals(
                       candidate.PcName,
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   !existing.AppVersion.Equals(candidate.AppVersion, StringComparison.Ordinal) ||
                   !existing.PreviousClubId.Equals(
                       candidate.PreviousClubId,
                       StringComparison.OrdinalIgnoreCase
                   );
        }

        private static async Task<bool> TryMarkClubAssignedAsync(PcIdentity identity)
        {
            try
            {
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var metadata = new
                {
                    id = identity.ClubId,
                    name = identity.ClubName,
                    isActivated = true,
                    installationId = identity.InstallationId,
                    pcName = Environment.MachineName,
                    activatedAt = identity.ActivatedAt,
                    appVersion = AppVersionService.Version,
                    updatedAt = now
                };

                await Task.WhenAll(
                    PatchAsync($"owner/clubs/{identity.ClubId}", metadata),
                    PatchAsync($"clubs/{identity.ClubId}/meta", metadata)
                ).ConfigureAwait(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TryMarkClubUnassignedAsync(string clubId)
        {
            try
            {
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var metadata = new Dictionary<string, object?>
                {
                    ["isActivated"] = false,
                    ["installationId"] = null,
                    ["pcName"] = null,
                    ["activatedAt"] = null,
                    ["updatedAt"] = now
                };

                await Task.WhenAll(
                    PatchAsync($"owner/clubs/{clubId}", metadata),
                    PatchAsync($"clubs/{clubId}/meta", metadata),
                    PatchAsync($"clubs/{clubId}/current/club", metadata)
                ).ConfigureAwait(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<FirebaseConditionalValue<T>> ReadConditionalAsync<T>(
            string path)
            where T : class
        {
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

            using HttpResponseMessage response = await HttpClient
                .SendAsync(request)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new FirebaseConditionalValue<T>
            {
                Value = IsNullJson(json)
                    ? null
                    : JsonSerializer.Deserialize<T>(json, JsonOptions),
                ETag = ReadEtag(response)
            };
        }

        private static async Task<T?> GetJsonAsync<T>(string path)
            where T : class
        {
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);

            using HttpResponseMessage response = await HttpClient
                .GetAsync(url)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return IsNullJson(json)
                ? null
                : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        private static async Task<bool> TryPutConditionalAsync<T>(
            string path,
            T value,
            string etag)
        {
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value, JsonOptions),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            request.Headers.TryAddWithoutValidation("If-Match", etag);

            using HttpResponseMessage response = await HttpClient
                .SendAsync(request)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                return false;

            response.EnsureSuccessStatusCode();
            return true;
        }

        private static async Task<bool> TryDeleteConditionalAsync(
            string path,
            string etag)
        {
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.TryAddWithoutValidation("If-Match", etag);

            using HttpResponseMessage response = await HttpClient
                .SendAsync(request)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                return false;

            response.EnsureSuccessStatusCode();
            return true;
        }

        private static async Task PatchAsync(string path, object value)
        {
            string url = await FirebaseAuthService
                .BuildDatabaseUrlAsync(path)
                .ConfigureAwait(false);

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value, JsonOptions),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            using HttpResponseMessage response = await HttpClient
                .SendAsync(request)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        private static async Task<FirebaseClubMeta?> ReadLegacyClubMetaAsync(string clubId)
        {
            return await GetJsonAsync<FirebaseClubMeta>($"clubs/{clubId}/meta")
                .ConfigureAwait(false);
        }

        private static string ChannelBindingPath(string clubId)
        {
            return $"channelBindings/{clubId}";
        }

        private static string InstallationBindingPath(string installationId)
        {
            return $"installationBindings/{installationId}";
        }

        private static string ReadEtag(HttpResponseMessage response)
        {
            if (response.Headers.ETag != null)
                return response.Headers.ETag.Tag;

            if (response.Headers.TryGetValues("ETag", out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            throw new InvalidOperationException("Firebase не вернул ETag канала.");
        }

        private static bool IsNullJson(string json)
        {
            return string.IsNullOrWhiteSpace(json) ||
                   json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, T> ToCaseInsensitiveDictionary<T>(
            Dictionary<string, T>? source)
        {
            return source == null
                ? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, T>(source, StringComparer.OrdinalIgnoreCase);
        }

        private static int AvailabilityOrder(FirebaseChannelAvailability availability)
        {
            return availability switch
            {
                FirebaseChannelAvailability.Current => 0,
                FirebaseChannelAvailability.Available => 1,
                _ => 2
            };
        }

        private static int ClubNumber(string clubId)
        {
            string digits = new string(clubId.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int number) ? number : int.MaxValue;
        }

        private static string DisplayPcName(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "другой ПК" : value.Trim();
        }

        private static string FirstNotEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static void SetAuthenticationFailure(PcIdentity identity)
        {
            SetStatus(
                FirebaseChannelBindingStatus.ForIdentity(
                    FirebaseChannelBindingState.AuthenticationRequired,
                    identity,
                    "Firebase отклонил сохраненный вход. Локальная касса продолжает работать."
                ),
                TimeSpan.FromSeconds(30)
            );
        }

        private static void SetTemporaryFailure(PcIdentity identity, string message)
        {
            SetStatus(
                FirebaseChannelBindingStatus.ForIdentity(
                    FirebaseChannelBindingState.Offline,
                    identity,
                    message
                ),
                TimeSpan.FromSeconds(10)
            );
        }

        private static void SetStatus(
            FirebaseChannelBindingStatus status,
            TimeSpan retryAfter)
        {
            lock (StatusLock)
            {
                _status = status;
                _nextCheckUtc = DateTime.UtcNow.Add(retryAfter);
            }
        }

        private sealed class FirebaseConditionalValue<T>
            where T : class
        {
            public T? Value { get; set; }
            public string ETag { get; set; } = "";
        }

        private sealed class FirebaseChannelBinding
        {
            public string ClubId { get; set; } = "";
            public string ClubName { get; set; } = "";
            public string InstallationId { get; set; } = "";
            public string AuthUid { get; set; } = "";
            public string PcName { get; set; } = "";
            public string AppVersion { get; set; } = "";
            public string ClaimedAt { get; set; } = "";
            public string ClaimedAtLocal { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
            public string UpdatedAtLocal { get; set; } = "";
        }

        private sealed class FirebaseInstallationBinding
        {
            public string InstallationId { get; set; } = "";
            public string ClubId { get; set; } = "";
            public string ClubName { get; set; } = "";
            public string AuthUid { get; set; } = "";
            public string PcName { get; set; } = "";
            public string AppVersion { get; set; } = "";
            public string BoundAt { get; set; } = "";
            public string BoundAtLocal { get; set; } = "";
            public string PreviousClubId { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
            public string UpdatedAtLocal { get; set; } = "";
        }

        private sealed class FirebaseOwnerClub
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsActivated { get; set; }
            public string InstallationId { get; set; } = "";
            public string PcName { get; set; } = "";
            public bool IsDeleted { get; set; }
        }

        private sealed class FirebaseClubMeta
        {
            public string InstallationId { get; set; } = "";
            public string PcName { get; set; } = "";
        }

        private sealed class InstallationBindingCheck
        {
            public bool Success { get; private set; }
            public FirebaseInstallationBinding? Binding { get; private set; }

            public static InstallationBindingCheck Ok(FirebaseInstallationBinding binding)
            {
                return new InstallationBindingCheck
                {
                    Success = true,
                    Binding = binding
                };
            }

            public static InstallationBindingCheck Fail()
            {
                return new InstallationBindingCheck();
            }
        }

        private enum ChannelReleaseResult
        {
            Released,
            AlreadyFree,
            OwnedByAnotherInstallation,
            RetryRequired
        }
    }

    public enum FirebaseChannelBindingState
    {
        Unknown,
        Checking,
        Bound,
        Conflict,
        Offline,
        AuthenticationRequired,
        Unassigned
    }

    public sealed class FirebaseChannelBindingStatus
    {
        public FirebaseChannelBindingState State { get; set; }
        public string ClubId { get; set; } = "";
        public string InstallationId { get; set; } = "";
        public string OccupiedInstallationId { get; set; } = "";
        public string OccupiedPcName { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public FirebaseChannelBindingStatus Clone()
        {
            return new FirebaseChannelBindingStatus
            {
                State = State,
                ClubId = ClubId,
                InstallationId = InstallationId,
                OccupiedInstallationId = OccupiedInstallationId,
                OccupiedPcName = OccupiedPcName,
                Message = Message,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }

        public static FirebaseChannelBindingStatus Create(
            FirebaseChannelBindingState state,
            string message)
        {
            return new FirebaseChannelBindingStatus
            {
                State = state,
                Message = message,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        public static FirebaseChannelBindingStatus ForIdentity(
            FirebaseChannelBindingState state,
            PcIdentity identity,
            string message)
        {
            return new FirebaseChannelBindingStatus
            {
                State = state,
                ClubId = identity.ClubId,
                InstallationId = identity.InstallationId,
                Message = message,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        public static FirebaseChannelBindingStatus ForConflict(
            PcIdentity identity,
            string occupiedInstallationId,
            string occupiedPcName,
            string message)
        {
            return new FirebaseChannelBindingStatus
            {
                State = FirebaseChannelBindingState.Conflict,
                ClubId = identity.ClubId,
                InstallationId = identity.InstallationId,
                OccupiedInstallationId = occupiedInstallationId,
                OccupiedPcName = occupiedPcName,
                Message = message,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }
    }

    public enum FirebaseChannelAvailability
    {
        Current,
        Available,
        Occupied
    }

    public sealed class FirebaseChannelOption
    {
        public string ClubId { get; set; } = "";
        public string ClubName { get; set; } = "";
        public FirebaseChannelAvailability Availability { get; set; }
        public string OccupiedPcName { get; set; } = "";

        public bool IsSelectable =>
            Availability != FirebaseChannelAvailability.Occupied;

        public bool IsCurrent =>
            Availability == FirebaseChannelAvailability.Current;

        public string DisplayText
        {
            get
            {
                return Availability switch
                {
                    FirebaseChannelAvailability.Current =>
                        $"{ClubName} ({ClubId}) | текущий канал",
                    FirebaseChannelAvailability.Available =>
                        $"{ClubName} ({ClubId}) | свободен",
                    _ =>
                        $"{ClubName} ({ClubId}) | занят: {OccupiedPcName}"
                };
            }
        }
    }

    public sealed class FirebaseChannelCatalogResult
    {
        public bool Success { get; set; }
        public IReadOnlyList<FirebaseChannelOption> Channels { get; set; } =
            Array.Empty<FirebaseChannelOption>();
        public string Message { get; set; } = "";

        public static FirebaseChannelCatalogResult Ok(
            IReadOnlyList<FirebaseChannelOption> channels,
            string message)
        {
            return new FirebaseChannelCatalogResult
            {
                Success = true,
                Channels = channels,
                Message = message
            };
        }

        public static FirebaseChannelCatalogResult Fail(string message)
        {
            return new FirebaseChannelCatalogResult
            {
                Success = false,
                Message = message
            };
        }
    }

    public sealed class FirebaseChannelSwitchResult
    {
        public bool Success { get; set; }
        public string ClubId { get; set; } = "";
        public string ClubName { get; set; } = "";
        public string Message { get; set; } = "";

        public static FirebaseChannelSwitchResult Ok(
            string clubId,
            string clubName,
            string message)
        {
            return new FirebaseChannelSwitchResult
            {
                Success = true,
                ClubId = clubId,
                ClubName = clubName,
                Message = message
            };
        }

        public static FirebaseChannelSwitchResult Fail(string message)
        {
            return new FirebaseChannelSwitchResult
            {
                Success = false,
                Message = message
            };
        }
    }
}

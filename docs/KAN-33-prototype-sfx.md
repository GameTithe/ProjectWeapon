# KAN-33 1구역 검증판 효과음

`Assets/ProjectWeapon/PrototypeAudio/PrototypeSfx.prefab`을 검증 씬에 배치하고 활성 AudioListener 한 개를 사용한다. 효과음은 화면 중앙의 2D 출력으로 재생한다. 기본 음량은 80%이며 `SetVolume(0~1)`과 `SetMuted(bool)`로 변경한다. [KAN-33](https://gametithe.atlassian.net/browse/KAN-33) · [KAN-32](https://gametithe.atlassian.net/browse/KAN-32)

## 승인된 사건 연결

| 확정 사건 | 호출 |
| --- | --- |
| 런 진입 | `BeginRun(runId)` |
| 기본공격 실행 수락 | `OnWeaponAttack(runId, activationId)`. 여러 타격을 내더라도 발동당 한 번 호출 |
| 적 또는 플레이어의 실제 피해 수락 | `OnEnemyHit(runId, applicationId, targetId)` 또는 `OnPlayerHit(runId, applicationId)`. 무적·빗나감·중복 요청은 제외 |
| 회피 시작 승인 | `OnDodge(runId, activationId)` |
| 런 슬롯에 줍기 또는 교체 반영 | `OnPickup(runId, confirmationId)`. 가방 선택 대기나 취소는 제외 |
| 귀환 시도 시작 | `OnExtractionStarted(runId, attempt.Id)` |
| 귀환 시도 취소·실패·성공 | `OnExtractionResolved(runId, attempt.Id, succeeded)`. 성공만 별도 종소리 재생 |
| 런 종료 | 결과 사건을 전달한 뒤 `EndRun(runId)` |

발동·피해·획득의 ID는 확정된 사건에 한 번 발급하고 재전달에도 그대로 사용한다. 적 피격은 같은 적용 ID라도 서로 다른 적의 실제 피해는 별도 사건이다. 귀환 시도 번호는 런에서 정한 증가 순서를 사용한다. 발동 결과를 확인한 뒤 같은 게임플레이 스레드에서 호출한다. [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-70](https://gametithe.atlassian.net/browse/KAN-70) · [KAN-80](https://gametithe.atlassian.net/browse/KAN-80) · [KAN-83](https://gametithe.atlassian.net/browse/KAN-83)

최대 네 일회성 소스에 더해 귀환 루프 전용 소스 한 개를 사용한다. 지난 100ms 동안 적 피격음은 최대 두 개, 플레이어 피격음은 한 개다. 소스가 모두 사용 중이면 플레이어 피격과 귀환 성공이 일반 공격·적 피격부터 대체한다. 음소거는 현재 출력 전부를 정지하고, 해제할 때 유효한 귀환 시도만 루프를 재개한다. 음소거나 한도 때문에 재생되지 않은 과거 사건은 다시 재생하지 않는다.

## 음원 원본과 재현

일회성 6개와 귀환 루프 1개를 이 프로젝트용으로 직접 합성했다. 외부 녹음·샘플·음원은 사용하지 않았다. 제작 원본과 절차는 `audio-source/GeneratePrototypeSfx.mjs`이고, KAN-21의 자체 제작 L1 범위에 해당한다. 원본은 단일 채널 24kHz 16비트 PCM WAV다. Unity는 원본 레이트와 PCM을 유지해 메모리에 풀어 둔다. [KAN-21](https://gametithe.atlassian.net/browse/KAN-21)

```powershell
node ./audio-source/GeneratePrototypeSfx.mjs
unity test . --mode PlayMode --filter ProjectWeapon.PrototypeAudio.Tests --output "$env:TEMP/KAN-33-playmode.xml"
```

PlayMode에서는 소스 점유와 피격 구간 한도, 높은 우선순위의 대체, 중복 시도 무시, 음소거와 활성 귀환 루프 재개, 임포트된 PCM의 피크 및 루프 끝 샘플을 검증한다. 실제 카메라가 있는 검증 씬에서 각 상호작용과 장치 출력의 소리를 대조하는 작업은 한 방 통합에서 진행한다. [KAN-197](https://gametithe.atlassian.net/browse/KAN-197)

# KAN-86 보스 처치와 다음 구역 해금

`BossDefeatTracker`는 탐험의 전투 조정자가 지정한 보스의 실제 사망 이벤트를 기록한다. 진행 중인 같은 탐험, 지정된 대상 ID 및 탐험 시간 경계를 확인한다. `ZoneProgression.TryUnlockNext`는 같은 탐험과 구역의 보스 처치가 성공 귀환 확정 시각까지 접수됐고, KAN-96의 같은 탐험 성공 영수증이 저장본에 확정됐을 때 다음 등록 구역을 연다. [KAN-86](https://gametithe.atlassian.net/browse/KAN-86) · [KAN-88](https://gametithe.atlassian.net/browse/KAN-88) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

첫 구역은 처음부터 열린다. 보스 없이 성공 귀환한 경우와 실패, 마지막 구역, 이미 처리된 낮은 구역은 해금 변경 없이 종료된다. KAN-173은 `ZoneProgression.IsUnlocked(zoneId)`와 `HighestUnlockedZoneId`를 읽는다. 권장 무기 등급은 해금 입력에 포함되지 않는다. [KAN-173](https://gametithe.atlassian.net/browse/KAN-173)

현재 폐성 출격 호스트는 실제 보스 사망을 같은 탐험의 추적기에 연결한다. 성공 귀환과 확인된 처치가 모두 있으면 기존 정산 영수증에 다음 구역 ID를 기록한다. 원자 저장 한 번으로 무기와 코인, 영수증, 해금을 확정하므로 쓰기 충돌이 발생하면 결과와 해금이 함께 재시도된다. 앱은 확정된 연속 해금만 오래된 저장본에 다시 적용한다. 구형 영수증 또는 보스를 건너뛴 성공으로 해금을 추정하지 않는다.

## 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.ZoneProgression.Tests.ZoneProgressionTests --output "$env:TEMP/KAN-86-zone-progression.xml"
```

성공한 정산과 같은 탐험의 보스 처치 증거를 넣으면 다음 구역 하나를 연다. 보스 없이 귀환하거나 정산 실패, 만료 또는 최종 구역이면 현재 해금을 유지한다. 위 검증은 임시 정산 저장소와 구역 카탈로그를 사용한다. 실제 폐성 귀환, 저장 잠금 뒤 재시도, 두 번째 로비 로드 뒤 해금 유지도 `ProjectWeapon.SortieLoop.Tests.SortieLoopTests` PlayMode에서 검증한다. 구역 선택 화면은 KAN-173에서 연결한다.

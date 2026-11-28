# KAN-98 폐성 보스 단일 충격파

[KAN-98](https://gametithe.atlassian.net/browse/KAN-98) · [KAN-86](https://gametithe.atlassian.net/browse/KAN-86)

`Assets/ProjectWeapon/Enemies/Boss/AbandonedCastleBoss.prefab`의 런타임 호스트는 현재 활성 RunState로 `Initialize`한 후 같은 탐험 시각과 플레이어 좌표를 `Advance`에 전달한다. `ShockwaveResolved`에서 플레이어 피해를 적용하고 `Defeated`에서 런 ID, 지정된 보스 ID, 확정 처치 시각을 받는다. 호스트에서 보스 생존과 독립적으로 기존 귀환 범위를 확인한다.

설정 에셋의 검증값은 체력 90, 탐지 2.0, 충격파 1.6, 피해 24, 예고 0.9초, 회복 1.5초다. 충격파는 시작 시 고정한 원으로 한 번 판정한다. 피격 시 빨간색, 사망 시 회색의 기존 Guard 스프라이트를 임시 표시로 쓴다.

PlayMode `BossShockwaveTests`에서 실제 프리팹 예고 및 위치·시간 경계, 무효 중복 피격, 처치로 예고 취소와 처치 1회, 보스 우회 귀환을 검증한다. 다음 구역 해금은 진행 서비스에서 처치와 성공 귀환을 확인한 뒤 결정한다.

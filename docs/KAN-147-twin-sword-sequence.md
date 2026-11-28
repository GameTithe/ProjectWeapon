# KAN-147 쌍검 기본공격

2등급 `twin_swords`는 `PrototypeTwinSwordsWeaponCatalog`에서 조회한다. `TwinSwordsSequence.asset` 하나가 양손 연속 타격 기본공격을 정의한다. 검증값은 거리 1.75유닛, 오른손 지연 150ms, 왼손 피해 7, 오른손 피해 9, 발동 간격 760ms다. [KAN-147](https://gametithe.atlassian.net/browse/KAN-147) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

스케줄러가 가장 가까운 유효 대상을 선택하면 콜백에서 `TryCreateSequence`로 한 발동을 만든다. 첫 타격은 발동 프레임의 `TryResolveNext`로 처리한다. 같은 탐험에서 오른손 타격이 예정된 시각에 도달하면 현재 공격자 좌표와 최초 표적의 최신 스냅샷을 넘긴다. 없어진 표적은 빈 스냅샷을 전달한다. 유효 표적이 현재 사거리 안에 있으면 각 타격이 다른 피해 적용 ID를 받는다. 프레임이 늦으면 실제 처리한 시각으로 피해를 요청한다. 귀환 채널링, 탐험 변경과 탐험 만료는 미발동 타격을 취소한다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

EditMode의 `ProjectWeapon.AutoAttack.Tests.TwinSwordSequenceTests`가 실제 카탈로그·스케줄러·피해 요청을 연결해 두 번의 타격, 바뀐 좌표와 사거리 경계, 표적 실패, 시각·쿨다운 경계 및 취소를 재현한다.

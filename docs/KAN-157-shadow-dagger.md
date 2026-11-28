# KAN-157 그림자 단검 기본공격

3등급 `shadow_dagger`는 별도 검증용 `PrototypeShadowDaggerWeaponCatalog`로 조회한다. 단일 `ShadowDaggerStrike.asset`의 검증값은 사거리 2유닛, 타격 지연 230ms, 피해 15, 발동 간격 900ms다. [KAN-157](https://gametithe.atlassian.net/browse/KAN-157) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

기존 스케줄러가 가장 가까운 유효 표적을 선택한다. 실행 콜백은 `TrySchedule`로 시전 원점, 대상 ID와 예약 시각을 고정한다. 같은 탐험의 갱신 단계는 최초 대상의 최신 스냅샷과 현재 시각을 `TryResolve`에 전달한다. 예정 시각에 도달하면 시전 원점 기준 사거리 안의 유효 대상에게 피해를 한 번 요청한다. 다른 대상은 선택하지 않는다. 대상이 유효하지 않으면 타격은 빗나가며 탐험 종료·기한 경계·다른 런은 예약을 취소한다. 실제 처리 시각은 `ShadowStrikeResolution.EffectiveAt`에서 읽는다. [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

EditMode의 `ProjectWeapon.AutoAttack.Tests.ShadowDaggerTests`는 실제 카탈로그·스케줄러와 피해 요청을 이어 사거리·예약 시각·쿨다운 경계, 같은 대상, 무효 접촉과 탐험 종료를 재현한다.

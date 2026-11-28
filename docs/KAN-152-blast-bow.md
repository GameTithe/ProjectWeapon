# KAN-152 폭발궁 기본공격

3등급 `blast_bow`는 별도 검증용 `PrototypeBlastBowWeaponCatalog`에서 조회한다. `BlastBowArrow.asset` 하나를 기본공격으로 사용한다. 검증값은 표적 탐색 6유닛, 발동 간격 1250ms, 화살 속도 초당 12유닛, 수명 650ms, 폭발 반경 1.25유닛, 폭발 피해 20이다. 값은 패턴 에셋에서 조정한다. [KAN-152](https://gametithe.atlassian.net/browse/KAN-152) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

스케줄러는 가장 가까운 유효 표적을 골라 발동마다 화살 한 발을 발사한다. 어댑터는 `Advance`, `ContactTarget`, `ContactWorld`에 같은 탐험의 시각과 그 화살의 적용 ID를 넘긴다. 첫 유효 표적 또는 차단 지형 접촉의 확인 좌표가 원형 폭발의 중심이다. 반환된 `BlastBowExplosion`에만 피해 요청이 들어 있다. 경계 안에 있는 생존 대상마다 한 번만 피해를 요청한다. 수명 만료, 탐험 종료나 취소로 사라진 화살에는 폭발이 없다. [KAN-72](https://gametithe.atlassian.net/browse/KAN-72) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

EditMode의 `ProjectWeapon.AutoAttack.Tests.BlastBowPatternTests`는 실제 카탈로그와 스케줄러를 연결한다. 접촉 위치를 대역으로 전달해 사거리, 발동 수, 피해 시각, 폭발 경계, 지형과 대상 간 중복, 무효 접촉, 수명, 탐험 취소와 쿨다운을 재현한다.

# KAN-140 화염검 기본공격

2등급 `flame_sword`의 `FlameSword.asset`은 검증용 `FlameSwordMeleeSlash.asset` 하나를 참조한다. 즉시 부채꼴 피해를 처리하는 공용 기본공격을 재사용한다. 검증값은 거리 2.15유닛, 각도 105°, 한 대상당 피해 14, 발동 간격 1050ms다. 이 값은 무기 에셋에서 조정한다. [KAN-140](https://gametithe.atlassian.net/browse/KAN-140) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54)

표적이 없으면 발동 없이 대기한다. 발동 시 선택 표적 방향으로 하나의 피해 묶음을 만들고, 각도와 거리 안에 있는 생존 후보마다 실제 피해 요청을 한 번만 생성한다. 검증은 무기 에셋을 기존 스케줄러에서 발동하고 관측한 피해 시간을 비교한다. 화염 아트와 상태 효과는 이 원형에서 설정하지 않는다.

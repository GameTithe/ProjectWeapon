# KAN-143 강철창 관통 찌르기

2등급 `steel_spear`의 기본 공격은 별도 `PiercingSpearPattern` 에셋을 사용한다. 기존 자동공격 스케줄러로 유효한 가장 가까운 표적을 골라 방향을 잠근 뒤 발동 시점에 한 번 피해를 계산한다. 반폭 0.22유닛 이내인 전방 직선과 사거리 원의 교집합에서 대상별 한 번만 적용한다. 검증값은 사거리 2.7유닛, 피해 14, 발동 간격 1050ms다. 동일 좌표에서 시작할 때는 방향을 정할 수 없으므로 같은 위치의 대상만 검사한다. [KAN-143](https://gametithe.atlassian.net/browse/KAN-143) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-77](https://gametithe.atlassian.net/browse/KAN-77)

Unity EditMode의 `PiercingSpearTests`에서 실제 무기 에셋을 불러온다. 사거리·직선 반폭 경계, 앞뒤 방향, 중복 콜라이더, 사망 후보와 쿨다운을 검증한다. 나무 창의 기존 1대상 규칙은 기존 에셋 경로를 유지한다.

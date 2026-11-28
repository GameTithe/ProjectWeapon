# KAN-86 보스 처치와 다음 구역 해금

`BossDefeatTracker`는 탐험의 전투 조정자가 지정한 보스의 실제 사망 이벤트를 기록한다. 진행 중인 같은 탐험, 지정된 대상 ID 및 탐험 시간 경계를 확인한다. `ZoneProgression.TryUnlockNext`는 같은 탐험과 구역의 보스 처치가 성공 귀환 확정 시각까지 접수됐고, KAN-96의 같은 탐험 성공 영수증이 저장본에 확정됐을 때 다음 등록 구역을 연다. [KAN-86](https://gametithe.atlassian.net/browse/KAN-86) · [KAN-88](https://gametithe.atlassian.net/browse/KAN-88) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

첫 구역은 처음부터 열린다. 보스 없이 성공 귀환한 경우와 실패, 마지막 구역, 이미 처리된 낮은 구역은 해금 변경 없이 종료된다. KAN-173은 `ZoneProgression.IsUnlocked(zoneId)`와 `HighestUnlockedZoneId`를 읽는다. 권장 무기 등급은 해금 입력에 포함되지 않는다. [KAN-173](https://gametithe.atlassian.net/browse/KAN-173)

저장 담당자는 이전에 확정한 최고 구역 ID를 생성자에 넘겨 읽기 모델을 복원할 수 있다. 현재 저장 스키마와 정산 영수증에는 구역·보스 증거 및 최고 해금 값이 없으므로, 재실행 간 자동 보존은 이후 저장 통합에서 연결해야 한다. 이 작업의 테스트는 실제 카탈로그, 보스 사망 이벤트, 성공·실패 정산 저장, 구형 영수증과 경계를 재현한다.

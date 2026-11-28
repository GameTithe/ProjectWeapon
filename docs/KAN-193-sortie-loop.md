# KAN-193 폐성 출격·귀환·재출격

[KAN-193](https://gametithe.atlassian.net/browse/KAN-193) · [KAN-91](https://gametithe.atlassian.net/browse/KAN-91) · [KAN-170](https://gametithe.atlassian.net/browse/KAN-170) · [KAN-186](https://gametithe.atlassian.net/browse/KAN-186)

Unity 6000.6.9f1에서 `Assets/Scenes/SortieLoop/SortieLoop.unity`를 열고 Play한다. 로비의 **출격**을 누르고 낡은 검을 장착한 뒤 **출격 확인**를 누른다. 첫 프로필은 이 검증판 전용 저장 경로에 낡은 검과 철퇴를 1개씩 받는다. 장비를 고르지 않고 출격할 때는 현재 탐험 ID의 무료 낡은 검을 대여한다.

WASD로 이동하고 Space로 회피한다. 낡은 검은 가까운 유효 적을 자동 공격한다. 아래쪽으로 벽을 돌아 상자에 접근한 뒤 E로 열고 목제 창에 접근해 줍는다. 경비를 먼저 처리하고 귀환진에서 E를 누른 뒤 이동·피격 없이 3초를 기다린다. 결과 화면에서 반입품과 획득품의 귀환을 확인하고 로비로 돌아간다. 새 출격에서는 기존 실물을 꺼내 쓰거나 빈 장비로 새 대여품을 받을 수 있다.

실패 경로는 상자를 얻은 뒤 적에게 사망하거나 제한시간이 지나게 둔다. 실제 정산 영수증에 반입품과 획득품의 상실이 표시되고 미반입품과 확정 코인은 보존된다. 같은 프레임에 제한시간 0초와 귀환 완료가 맞물리면 시간 초과 영수증 하나만 저장한다. 정산에 실패하면 동일 런의 저장된 수치로 재시도한다.

검증 프로필은 `Application.persistentDataPath/ZoneOnePrototype/snapshot.json`에 저장한다. 로비에서 반입 확정 전에 다른 저장이 진행되면 새 상태를 읽고 장비 화면을 다시 연다. 입장 시작 실패는 동일 예약으로 재시도한다. 전투 중 증거 없이 씬이 사라지거나 예약만 남은 상태에서 새 프로세스를 열면 저장 복구 대기로 표시한다. 전체 라이프사이클 체크포인트와 실제 기기 강종은 별도 통합 범위다. [KAN-189](https://gametithe.atlassian.net/browse/KAN-189) · [KAN-37](https://gametithe.atlassian.net/browse/KAN-37)

PlayMode `SortieLoopTests`는 실제 로비와 장비 버튼 레이캐스트, 낡은 검 타격, E 사용 및 물리 픽업, 이동·실제 피격 취소, 마감과 귀환 완료의 경합, 창고·영수증, 무료 대여 재출격, 경쟁 저장 뒤 최신 로비에 한 번 출격을 검증한다. 저장과 시간축은 각 테스트에 분리한다.

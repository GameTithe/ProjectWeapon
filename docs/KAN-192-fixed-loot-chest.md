# KAN-192 폐성 테스트 상자

`Assets/ProjectWeapon/Chest/AbandonedCastleTestChest.prefab`에 원본 픽셀 열림·닫힘 스프라이트와 일반 나무 창이 지정돼 있다. 탐험 시작 뒤 씬의 상자에 `BindRun`을 한 번 호출하고 상자 앞 상호작용에서 `TryOpen`을 호출한다. 상자 인스턴스 하나는 현재 탐험에 묶인다. 새 탐험에서는 새 상자 인스턴스를 만들고, 위치·상호작용은 1구역 배치에서 연결한다. [KAN-192](https://gametithe.atlassian.net/browse/KAN-192) · [KAN-91](https://gametithe.atlassian.net/browse/KAN-91)

유효한 첫 개봉에서 서로 다른 상자와 공유되지 않는 신규 실물 ID 하나를 생성한다. 기존 `WorldLoot` 줍기와 가방 비교가 주운 물건을 런 가방에 반영한다. 실제 성공 정산을 확정하면 창고와 신규 획득 영수증에 들어가며, 사망 시 버려진다. 픽셀 윤곽의 정수 스케일을 유지하기 위해 32 PPU Point 임포트를 사용했다. 원본은 `art-source/Export-FixedLootChest.ps1`이다. [KAN-66](https://gametithe.atlassian.net/browse/KAN-66) · [KAN-80](https://gametithe.atlassian.net/browse/KAN-80) · [KAN-96](https://gametithe.atlassian.net/browse/KAN-96)

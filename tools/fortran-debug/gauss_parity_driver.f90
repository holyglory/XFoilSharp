program gauss_parity_driver
  implicit none

  integer :: case_count
  integer :: case_index
  integer :: row
  integer :: col
  integer :: current_case
  real :: matrix(4, 4)
  real :: rhs(4, 1)

  common /gauss_driver_state/ current_case

  read(*, *) case_count
  write(*, '(I0)') case_count

  do case_index = 1, case_count
    current_case = case_index

    do row = 1, 4
      do col = 1, 4
        call read_hex_real(matrix(row, col))
      end do
    end do

    do row = 1, 4
      call read_hex_real(rhs(row, 1))
    end do

    call GAUSS(4, 4, matrix, rhs, 1)
  end do

contains

  subroutine read_hex_real(value)
    implicit none

    real, intent(out) :: value
    character(len=8) :: token
    integer :: bits

    read(*, *) token
    read(token, '(Z8)') bits
    value = transfer(bits, value)
  end subroutine read_hex_real
end program gauss_parity_driver

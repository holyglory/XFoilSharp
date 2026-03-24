subroutine trace_gauss_state(scope, phase, np, krow, &
    row11, row12, row13, row14, &
    row21, row22, row23, row24, &
    row31, row32, row33, row34, &
    row41, row42, row43, row44, &
    rhs1, rhs2, rhs3, rhs4)
  implicit none

  character(len=*), intent(in) :: scope
  character(len=*), intent(in) :: phase
  integer, intent(in) :: np
  integer, intent(in) :: krow
  real, intent(in) :: row11
  real, intent(in) :: row12
  real, intent(in) :: row13
  real, intent(in) :: row14
  real, intent(in) :: row21
  real, intent(in) :: row22
  real, intent(in) :: row23
  real, intent(in) :: row24
  real, intent(in) :: row31
  real, intent(in) :: row32
  real, intent(in) :: row33
  real, intent(in) :: row34
  real, intent(in) :: row41
  real, intent(in) :: row42
  real, intent(in) :: row43
  real, intent(in) :: row44
  real, intent(in) :: rhs1
  real, intent(in) :: rhs2
  real, intent(in) :: rhs3
  real, intent(in) :: rhs4
  integer :: current_case

  common /gauss_driver_state/ current_case

  write(*, '(I0,1X,A,1X,I0,1X,I0,20(1X,Z8.8))') &
    current_case, trim(adjustl(phase)), np, krow, &
    transfer(row11, 0), transfer(row12, 0), transfer(row13, 0), transfer(row14, 0), &
    transfer(row21, 0), transfer(row22, 0), transfer(row23, 0), transfer(row24, 0), &
    transfer(row31, 0), transfer(row32, 0), transfer(row33, 0), transfer(row34, 0), &
    transfer(row41, 0), transfer(row42, 0), transfer(row43, 0), transfer(row44, 0), &
    transfer(rhs1, 0), transfer(rhs2, 0), transfer(rhs3, 0), transfer(rhs4, 0)
end subroutine trace_gauss_state

subroutine trace_array3_index()
end subroutine trace_array3_index

subroutine trace_enter()
end subroutine trace_enter

subroutine trace_exit()
end subroutine trace_exit

subroutine trace_lu_backsub_row()
end subroutine trace_lu_backsub_row

subroutine trace_lu_backsub_term()
end subroutine trace_lu_backsub_term

subroutine trace_lu_decompose_term()
end subroutine trace_lu_decompose_term

subroutine trace_lu_pivot()
end subroutine trace_lu_pivot

subroutine trace_matrix_entry()
end subroutine trace_matrix_entry

subroutine trace_text()
end subroutine trace_text
